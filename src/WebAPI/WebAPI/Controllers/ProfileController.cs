using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebAPI.Database;
using WebAPI.Models;

namespace WebAPI.Controllers;

/// <summary>
/// Core user-profile and role-registration endpoints: viewing/updating a user's
/// base profile, adding/removing roles, the per-role registration forms (PJ, PH,
/// Farmer, VG, Hunter, AP) and their matching get/update pairs, PH day-rate pricing,
/// and per-role subscription management/payment. One controller because all of it
/// hangs off the same userId/userRoleId identity model.
/// </summary>
[Authorize]
[ApiController]
[Route("api/profile")]
public class ProfileController : ControllerBase
{
    private readonly Profile_db _db;
    private readonly ResourceOwnership_db? _ownership;
    public ProfileController(Profile_db db, ResourceOwnership_db? ownership = null) { _db = db; _ownership = ownership; }

    /// <summary>
    /// Whether the caller owns the given userRoles row (or is an Admin). Role-detail rows
    /// hold licence, tax and ID numbers, so both reads and writes need this. Fails closed
    /// when the ownership service isn't available.
    /// </summary>
    private async Task<bool> OwnsUserRoleAsync(int userRoleId)
        => User.IsAdmin() || (_ownership != null && await User.OwnsUserRoleOrAdminAsync(_ownership, userRoleId));

    private ObjectResult RoleAccessDenied() => StatusCode(403, new { message = "Geen toegang tot hierdie rol se besonderhede nie." });

    /// <summary>
    /// Whether the caller may act on targetUserId's profile: the caller must be
    /// targetUserId themselves or an Admin. Unauthenticated callers never pass.
    /// </summary>
    private bool IsAuthorizedForUser(int targetUserId)
    {
        if (HttpContext == null || User?.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        var userIdClaim = User.FindFirst("sub")?.Value 
            ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("userId")?.Value;

        if (int.TryParse(userIdClaim, out var parsedClaimId) && parsedClaimId > 0)
        {
            if (parsedClaimId == targetUserId) return true;
            bool isAdmin = User.IsInRole("Admin") 
                || User.IsInRole("Administrator") 
                || User.IsInRole("Administrateur")
                || string.Equals(User.FindFirst("role")?.Value, "Admin", StringComparison.OrdinalIgnoreCase)
                || string.Equals(User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value, "Admin", StringComparison.OrdinalIgnoreCase);
            return isAdmin;
        }

        return false;
    }

    /// <summary>
    /// Fetches a user's base profile (name, picture, assigned roles). Public, because the
    /// public profile page needs it - but email, phone and ID/passport number are only
    /// returned to the user themselves or an Admin.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("getProfile/{userId}")]
    public async Task<IActionResult> GetProfile(int userId)
    {
        var profile = await _db.GetProfile(userId);
        if (profile == null)
            return NotFound();
        if (!IsAuthorizedForUser(userId))
        {
            profile.Email = string.Empty;
            profile.Phone = null;
            profile.IdPassportNum = null;
        }
        return Ok(profile);
    }

    /// <summary>Updates a user's base profile fields.</summary>
    [HttpPut("updateProfile/{userId}")]
    public async Task<IActionResult> UpdateProfile(int userId, [FromBody] UpdateProfileRequest request)
    {
        if (!IsAuthorizedForUser(userId)) return StatusCode(403, new { message = "Geen toegang om hierdie profiel op te dateer nie." });
        if (request == null) return BadRequest(new { message = "Geen profielbesonderhede ontvang nie." });
        var invalid = UpdateProfileValidator.Validate(request);
        if (invalid != null) return BadRequest(new { message = invalid });
        var ok = await _db.UpdateProfile(userId, request);
        return ok ? Ok(new { Message = "Profile updated" }) : BadRequest(new { Message = "Update failed" });
    }

    /// <summary>Grants a user an additional role by code (e.g. "PJ", "PH"). Registration details for that role are filled in separately via the matching RegisterXxx endpoint.</summary>
    [HttpPost("addRole/{userId}")]
    public async Task<IActionResult> AddRole(int userId, [FromBody] AddRoleRequest request)
    {
        if (!IsAuthorizedForUser(userId)) return StatusCode(403, new { message = "Geen toegang om rolle vir hierdie gebruiker by te voeg nie." });
        if (CallerIdentity.IsAdminRoleCode(request.RoleCode) && !User.IsAdmin())
            return StatusCode(403, new { message = "Slegs 'n administrateur kan die administrateur-rol toeken." });
        var id = await _db.AddRole(userId, request.RoleCode);
        if (id == 0) return BadRequest(new { message = "Role not found" });
        if (id == -1) return BadRequest(new { message = "Role already assigned" });
        return Ok(new { UserRoleId = id });
    }

    /// <summary>Removes a role assignment by its userRoles row id.</summary>
    [HttpDelete("deleteRole/{userId}/{roleId}")]
    public async Task<IActionResult> DeleteRole(int userId, int roleId)
    {
        if (!IsAuthorizedForUser(userId)) return StatusCode(403, new { message = "Geen toegang om rolle vir hierdie gebruiker te skrap nie." });
        var ok = await _db.DeleteRole(userId, roleId);
        return ok ? Ok(new { success = true }) : NotFound();
    }

    /// <summary>Removes a role assignment by role code instead of row id.</summary>
    [HttpDelete("deleteRoleByCode/{userId}/{roleCode}")]
    public async Task<IActionResult> DeleteRoleByCode(int userId, string roleCode)
    {
        if (!IsAuthorizedForUser(userId)) return StatusCode(403, new { message = "Geen toegang om rolle vir hierdie gebruiker te skrap nie." });
        var ok = await _db.DeleteRoleByCode(userId, roleCode);
        return ok ? Ok(new { success = true }) : NotFound();
    }

    /// <summary>Deletes a user account entirely.</summary>
    [HttpDelete("deleteUser/{userId}")]
    public async Task<IActionResult> DeleteUser(int userId)
    {
        if (!IsAuthorizedForUser(userId)) return StatusCode(403, new { message = "Geen toegang om hierdie gebruiker te skrap nie." });
        var ok = await _db.DeleteUser(userId);
        return ok ? Ok(new { success = true, message = "Gebruiker suksesvol verwyder" }) : BadRequest(new { success = false, message = "Kon nie gebruiker verwyder nie" });
    }

    /// <summary>
    /// Validates and saves the PJ (outfitter) registration form. AddRole is called
    /// first to (idempotently) assign the PJ role - if it's already assigned
    /// (AddRole returns -1), the existing userRoleId is looked up instead, so this
    /// endpoint doubles as both first-time registration and re-submission. This
    /// idempotent-AddRole-then-GetUserRoleId pattern repeats for every RegisterXxx
    /// endpoint below (PH, Farmer, VG, Hunter, AP).
    /// </summary>
    [HttpPost("registerPj/{userId}")]
    public async Task<IActionResult> RegisterPj(int userId, [FromBody] RegisterPjRequest request)
    {
        if (!IsAuthorizedForUser(userId)) return StatusCode(403, new { message = "Geen toegang om hierdie rol vir die gebruiker te registreer nie." });
        if (request == null) return BadRequest(new { message = "Ongeldige versoekdata." });
        if (string.IsNullOrWhiteSpace(request.BusinessName)) return BadRequest(new { message = "Uitruster besigheidsnaam is verpligtend." });
        if (string.IsNullOrWhiteSpace(request.BusinessRegistrationNumber)) return BadRequest(new { message = "CIPC registrasienommer is verpligtend." });
        if (string.IsNullOrWhiteSpace(request.TaxPinVat)) return BadRequest(new { message = "Belasting Pin / BTW-nommer is verpligtend." });
        if (string.IsNullOrWhiteSpace(request.PhysicalAddress)) return BadRequest(new { message = "Fisiese besigheidsadres is verpligtend." });
        if (request.YearsOfExperience < 0 || request.YearsOfExperience > 70) return BadRequest(new { message = "Jare ondervinding moet tussen 0 en 70 wees." });

        var id = await _db.AddRole(userId, "PJ");
        if (id == -1)
        {
            id = await _db.GetUserRoleId(userId, "PJ");
        }
        if (id <= 0) return BadRequest(new { message = "Kon nie rol toeken nie." });
        await _db.RegisterPj(id, request);
        return Ok(new { UserRoleId = id });
    }

    /// <summary>Validates and saves the PH (professional hunter) registration form.</summary>
    [HttpPost("registerPh/{userId}")]
    public async Task<IActionResult> RegisterPh(int userId, [FromBody] RegisterPhRequest request)
    {
        if (!IsAuthorizedForUser(userId)) return StatusCode(403, new { message = "Geen toegang om hierdie rol vir die gebruiker te registreer nie." });
        if (request == null) return BadRequest(new { message = "Ongeldige versoekdata." });
        if (string.IsNullOrWhiteSpace(request.AssociationMembershipNumber)) return BadRequest(new { message = "Lidmaatskapnommer van vereniging is verpligtend." });
        if (string.IsNullOrWhiteSpace(request.FirstAidCertification)) return BadRequest(new { message = "Noodhulp sertifisering is verpligtend." });
        if (string.IsNullOrWhiteSpace(request.FirearmCompetencyLicenses)) return BadRequest(new { message = "Vuurwapenbevoegdheid en lisensies is verpligtend." });
        if (request.YearsOfExperience < 0 || request.YearsOfExperience > 70) return BadRequest(new { message = "Jare ondervinding as PH moet tussen 0 en 70 wees." });
        if (request.HasDangerousGameEndorsement && string.IsNullOrWhiteSpace(request.DangerousGameDetails))
            return BadRequest(new { message = "Dangerous Game besonderhede is verpligtend wanneer Dangerous Game gekies is." });

        var id = await _db.AddRole(userId, "PH");
        if (id == -1)
        {
            id = await _db.GetUserRoleId(userId, "PH");
        }
        if (id <= 0) return BadRequest(new { message = "Kon nie rol toeken nie." });
        await _db.RegisterPh(id, request);
        return Ok(new { UserRoleId = id });
    }

    /// <summary>Registers the Farmer (Boer) role. Unlike the other RegisterXxx endpoints, this one has no field-level validation - RegisterBoerRequest's fields are all optional here.</summary>
    [HttpPost("registerFarmer/{userId}")]
    public async Task<IActionResult> RegisterFarmer(int userId, [FromBody] RegisterBoerRequest request)
    {
        if (!IsAuthorizedForUser(userId)) return StatusCode(403, new { message = "Geen toegang om hierdie rol vir die gebruiker te registreer nie." });
        if (request == null) return BadRequest(new { message = "Ongeldige versoekdata." });
        var id = await _db.AddRole(userId, "FARMER");
        if (id == -1)
        {
            id = await _db.GetUserRoleId(userId, "FARMER");
        }
        if (id <= 0) return BadRequest(new { message = "Kon nie rol toeken nie." });
        await _db.RegisterFarmer(id, request);
        return Ok(new { UserRoleId = id });
    }

    /// <summary>Validates and saves the VG (videographer) registration form.</summary>
    [HttpPost("registerVg/{userId}")]
    public async Task<IActionResult> RegisterVg(int userId, [FromBody] RegisterVgRequest request)
    {
        if (!IsAuthorizedForUser(userId)) return StatusCode(403, new { message = "Geen toegang om hierdie rol vir die gebruiker te registreer nie." });
        if (request == null) return BadRequest(new { message = "Ongeldige versoekdata." });
        if (string.IsNullOrWhiteSpace(request.CompanyName)) return BadRequest(new { message = "Ateljee- of maatskappynaam is verpligtend." });
        if (string.IsNullOrWhiteSpace(request.SacaaRegistrationNumber)) return BadRequest(new { message = "SACAA registrasienommer is verpligtend." });
        if (string.IsNullOrWhiteSpace(request.RplLicenseNumber)) return BadRequest(new { message = "RPL vlieënierslisensienommer is verpligtend." });
        if (request.YearsOfExperience < 0 || request.YearsOfExperience > 70) return BadRequest(new { message = "Jare ondervinding moet tussen 0 en 70 wees." });

        var id = await _db.AddRole(userId, "VG");
        if (id == -1)
        {
            id = await _db.GetUserRoleId(userId, "VG");
        }
        if (id <= 0) return BadRequest(new { message = "Kon nie rol toeken nie." });
        await _db.RegisterVg(id, request);
        return Ok(new { UserRoleId = id });
    }

    /// <summary>
    /// Validates and saves the Hunter registration form. The passport/ID number
    /// must match an MRZ-style pattern (letters, digits, and '<' padding, 5-17
    /// chars) - matches the machine-readable-zone format used on passports.
    /// </summary>
    [HttpPost("registerHunter/{userId}")]
    public async Task<IActionResult> RegisterHunter(int userId, [FromBody] RegisterHunterRequest request)
    {
        if (!IsAuthorizedForUser(userId)) return StatusCode(403, new { message = "Geen toegang om hierdie rol vir die gebruiker te registreer nie." });
        if (request == null) return BadRequest(new { message = "Ongeldige versoekdata." });
        if (string.IsNullOrWhiteSpace(request.Username)) return BadRequest(new { message = "Jagter bynaam vir ranglys is verpligtend." });
        if (string.IsNullOrWhiteSpace(request.IdPassportNumber)) return BadRequest(new { message = "Paspoortnommer is verpligtend." });
        if (!System.Text.RegularExpressions.Regex.IsMatch(request.IdPassportNumber.Trim().ToUpperInvariant(), @"^[A-Z0-9<]{5,17}$"))
            return BadRequest(new { message = "Ongeldige paspoortnommer formaat (moet 5 tot 17 letters, syfers of '<' karakters bevat)." });
        if (string.IsNullOrWhiteSpace(request.Country)) return BadRequest(new { message = "Land van woon is verpligtend." });

        var id = await _db.AddRole(userId, "HUNTER");
        if (id == -1)
        {
            id = await _db.GetUserRoleId(userId, "HUNTER");
        }
        if (id <= 0) return BadRequest(new { message = "Kon nie rol toeken nie." });
        await _db.RegisterHunter(id, request);
        return Ok(new { UserRoleId = id });
    }

    /// <summary>Activates the Advertiser (AP) role - no registration form fields, just a role grant.</summary>
    [HttpPost("registerAp/{userId}")]
    public async Task<IActionResult> RegisterAp(int userId)
    {
        if (!IsAuthorizedForUser(userId)) return StatusCode(403, new { message = "Geen toegang om die AP-rol vir hierdie gebruiker te aktiveer nie." });
        var id = await _db.AddRole(userId, "AP");
        if (id == -1)
        {
            id = await _db.GetUserRoleId(userId, "AP");
        }
        if (id <= 0) return BadRequest(new { message = "Kon nie rol toeken nie." });
        await _db.RegisterAp(id);
        return Ok(new { UserRoleId = id });
    }

    /// <summary>Fetches a PJ's registration/business details - only the role's owner or an Admin.</summary>
    [HttpGet("getPj/{userRoleId}")]
    public async Task<IActionResult> GetPj(int userRoleId)
        => await OwnsUserRoleAsync(userRoleId) ? Ok(await _db.GetPj(userRoleId)) : RoleAccessDenied();

    /// <summary>Updates an existing PJ's registration details (same validation as RegisterPj). Only the role's owner or an Admin.</summary>
    [HttpPut("updatePj/{userRoleId}")]
    public async Task<IActionResult> UpdatePj(int userRoleId, [FromBody] RegisterPjRequest request)
    {
        if (!await OwnsUserRoleAsync(userRoleId)) return RoleAccessDenied();
        if (request == null) return BadRequest(new { message = "Ongeldige versoekdata." });
        if (string.IsNullOrWhiteSpace(request.BusinessName)) return BadRequest(new { message = "Uitruster besigheidsnaam is verpligtend." });
        if (string.IsNullOrWhiteSpace(request.BusinessRegistrationNumber)) return BadRequest(new { message = "CIPC registrasienommer is verpligtend." });
        if (string.IsNullOrWhiteSpace(request.TaxPinVat)) return BadRequest(new { message = "Belasting Pin / BTW-nommer is verpligtend." });
        if (string.IsNullOrWhiteSpace(request.PhysicalAddress)) return BadRequest(new { message = "Fisiese besigheidsadres is verpligtend." });
        if (request.YearsOfExperience < 0 || request.YearsOfExperience > 70) return BadRequest(new { message = "Jare ondervinding moet tussen 0 en 70 wees." });
        return Ok(await _db.UpdatePj(userRoleId, request));
    }

    /// <summary>Fetches a PH's registration/qualification details - only the role's owner or an Admin.</summary>
    [HttpGet("getPh/{userRoleId}")]
    public async Task<IActionResult> GetPh(int userRoleId)
        => await OwnsUserRoleAsync(userRoleId) ? Ok(await _db.GetPh(userRoleId)) : RoleAccessDenied();

    /// <summary>Updates an existing PH's registration details (same validation as RegisterPh). Only the role's owner or an Admin.</summary>
    [HttpPut("updatePh/{userRoleId}")]
    public async Task<IActionResult> UpdatePh(int userRoleId, [FromBody] RegisterPhRequest request)
    {
        if (!await OwnsUserRoleAsync(userRoleId)) return RoleAccessDenied();
        if (request == null) return BadRequest(new { message = "Ongeldige versoekdata." });
        if (string.IsNullOrWhiteSpace(request.AssociationMembershipNumber)) return BadRequest(new { message = "Lidmaatskapnommer van vereniging is verpligtend." });
        if (string.IsNullOrWhiteSpace(request.FirstAidCertification)) return BadRequest(new { message = "Noodhulp sertifisering is verpligtend." });
        if (string.IsNullOrWhiteSpace(request.FirearmCompetencyLicenses)) return BadRequest(new { message = "Vuurwapenbevoegdheid en lisensies is verpligtend." });
        if (request.YearsOfExperience < 0 || request.YearsOfExperience > 70) return BadRequest(new { message = "Jare ondervinding as PH moet tussen 0 en 70 wees." });
        return Ok(await _db.UpdatePh(userRoleId, request));
    }

    /// <summary>Fetches a Farmer's registration details - only the role's owner or an Admin.</summary>
    [HttpGet("getFarmer/{userRoleId}")]
    public async Task<IActionResult> GetFarmer(int userRoleId)
        => await OwnsUserRoleAsync(userRoleId) ? Ok(await _db.GetFarmer(userRoleId)) : RoleAccessDenied();

    /// <summary>Updates an existing Farmer's registration details. Only the role's owner or an Admin.</summary>
    [HttpPut("updateFarmer/{userRoleId}")]
    public async Task<IActionResult> UpdateFarmer(int userRoleId, [FromBody] RegisterBoerRequest request)
        => await OwnsUserRoleAsync(userRoleId) ? Ok(await _db.UpdateFarmer(userRoleId, request)) : RoleAccessDenied();

    /// <summary>Fetches a VG's registration/equipment details - only the role's owner or an Admin.</summary>
    [HttpGet("getVg/{userRoleId}")]
    public async Task<IActionResult> GetVg(int userRoleId)
        => await OwnsUserRoleAsync(userRoleId) ? Ok(await _db.GetVg(userRoleId)) : RoleAccessDenied();

    /// <summary>Updates an existing VG's registration details (same validation as RegisterVg). Only the role's owner or an Admin.</summary>
    [HttpPut("updateVg/{userRoleId}")]
    public async Task<IActionResult> UpdateVg(int userRoleId, [FromBody] RegisterVgRequest request)
    {
        if (!await OwnsUserRoleAsync(userRoleId)) return RoleAccessDenied();
        if (request == null) return BadRequest(new { message = "Ongeldige versoekdata." });
        if (string.IsNullOrWhiteSpace(request.CompanyName)) return BadRequest(new { message = "Ateljee- of maatskappynaam is verpligtend." });
        if (string.IsNullOrWhiteSpace(request.SacaaRegistrationNumber)) return BadRequest(new { message = "SACAA registrasienommer is verpligtend." });
        if (string.IsNullOrWhiteSpace(request.RplLicenseNumber)) return BadRequest(new { message = "RPL vlieënierslisensienommer is verpligtend." });
        if (request.YearsOfExperience < 0 || request.YearsOfExperience > 70) return BadRequest(new { message = "Jare ondervinding moet tussen 0 en 70 wees." });
        return Ok(await _db.UpdateVg(userRoleId, request));
    }

    /// <summary>Fetches a Hunter's registration details - only the role's owner or an Admin.</summary>
    [HttpGet("getHunter/{userRoleId}")]
    public async Task<IActionResult> GetHunter(int userRoleId)
        => await OwnsUserRoleAsync(userRoleId) ? Ok(await _db.GetHunter(userRoleId)) : RoleAccessDenied();

    /// <summary>
    /// Updates an existing Hunter's registration details. Note: unlike RegisterHunter,
    /// this does not re-validate the passport/ID number's MRZ format - only presence.
    /// </summary>
    [HttpPut("updateHunter/{userRoleId}")]
    public async Task<IActionResult> UpdateHunter(int userRoleId, [FromBody] RegisterHunterRequest request)
    {
        if (!await OwnsUserRoleAsync(userRoleId)) return RoleAccessDenied();
        if (request == null) return BadRequest(new { message = "Ongeldige versoekdata." });
        if (string.IsNullOrWhiteSpace(request.Username)) return BadRequest(new { message = "Jagter bynaam vir ranglys is verpligtend." });
        if (string.IsNullOrWhiteSpace(request.IdPassportNumber)) return BadRequest(new { message = "ID- of paspoortnommer is verpligtend." });
        if (string.IsNullOrWhiteSpace(request.Country)) return BadRequest(new { message = "Land van woon is verpligtend." });
        return Ok(await _db.UpdateHunter(userRoleId, request));
    }

    /// <summary>Fetches a PH's standard and dangerous-game day rates (public - shown on the PH's profile).</summary>
    [AllowAnonymous]
    [HttpGet("phRates/{userId}")]
    public async Task<IActionResult> GetPhDailyRates(int userId)
    {
        var rates = await _db.GetPhDailyRates(userId);
        return Ok(rates);
    }

    /// <summary>Saves a PH's day rates - both standard and dangerous-game rates are required together.</summary>
    [HttpPost("phRates/{userId}")]
    public async Task<IActionResult> SavePhDailyRates(int userId, [FromBody] PhDailyRatesDto dto)
    {
        if (!IsAuthorizedForUser(userId)) return StatusCode(403, new { message = "Geen toegang om tariewe op te dateer nie." });
        if (dto.StandardDailyRate == null || dto.DangerousGameDailyRate == null)
            return BadRequest(new { message = "Beide standaard- en gevaarlike-wild-tariewe is verpligtend." });

        var ok = await _db.SavePhDailyRates(userId, dto.StandardDailyRate.Value, dto.DangerousGameDailyRate.Value, dto.HasDangerousGameAccreditation);
        return ok ? Ok(new { message = "PH dagtariewe suksesvol opgedateer." }) : BadRequest(new { message = "Opdatering van dagtariewe het misluk." });
    }

    /// <summary>Fetches a user's subscription record for a given role (e.g. PJ monthly listing fee status) - only that user or an Admin.</summary>
    [HttpGet("subscription/{userId}/{roleCode}")]
    public async Task<IActionResult> GetRoleSubscription(int userId, string roleCode)
    {
        if (!IsAuthorizedForUser(userId)) return StatusCode(403, new { message = "Geen toegang tot hierdie intekening nie." });
        var sub = await _db.GetRoleSubscription(userId, roleCode);
        return Ok(sub);
    }

    /// <summary>
    /// Updates a role subscription. An Admin may overwrite the whole record (manual
    /// correction). The owner may only pause or resume it (IsActive), and resuming needs a
    /// paid period that is still running - paying is done through api/payments. The route's
    /// userId/roleCode always win over anything in the body.
    /// </summary>
    [HttpPost("subscription/{userId}/{roleCode}")]
    public async Task<IActionResult> SaveRoleSubscription(int userId, string roleCode, [FromBody] RoleSubscriptionDto sub)
    {
        if (!IsAuthorizedForUser(userId)) return StatusCode(403, new { message = "Geen toegang om intekening op te dateer nie." });
        if (sub == null) return BadRequest(new { message = "Ongeldige versoek." });

        if (User.IsInRole("Admin"))
        {
            sub.UserId = userId;
            sub.RoleCode = roleCode;
            var ok = await _db.SaveRoleSubscription(sub);
            return ok ? Ok(sub) : BadRequest(new { message = "Opdatering van intekening het misluk." });
        }

        if (!await _db.SetSubscriptionActive(userId, roleCode, sub.IsActive))
            return Conflict(new { message = sub.IsActive
                ? "U advertensie kan slegs heraktiveer word terwyl 'n betaalde tydperk nog loop. Betaal asseblief eers."
                : "Geen intekening gevind nie." });
        return Ok(await _db.GetRoleSubscription(userId, roleCode));
    }

    /// <summary>Admin-only: credits a subscription payment by hand (e.g. reconciling an EFT). Users pay through api/payments.</summary>
    [Authorize(Roles = "Admin")]
    [HttpPost("subscription/processPayment")]
    public async Task<IActionResult> ProcessSubscriptionPayment([FromBody] ProcessSubscriptionPaymentRequest req)
    {
        if (req == null || req.UserId <= 0) return BadRequest(new { message = "Ongeldige betalingsversoek." });
        var sub = await _db.ProcessSubscriptionPayment(req.UserId, req.RoleCode, req.DurationMonths, req.Amount, req.PaymentReference);
        return Ok(sub);
    }
}

/// <summary>Payload for AddRole - which role code to grant.</summary>
public class AddRoleRequest
{
    public string RoleCode { get; set; } = string.Empty;
}

