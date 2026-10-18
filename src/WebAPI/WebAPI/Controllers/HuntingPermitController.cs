using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebAPI.Database;
using WebAPI.Models;

namespace WebAPI.Controllers;

/// <summary>
/// SAPS hunting-permit application workflow: submitting a new application, listing/
/// reviewing existing ones, and the SAPS 520 firearm-permit form submission (which
/// is simulated - see SubmitSaps520 - rather than wired to a real SAPS integration).
/// </summary>
[Authorize]
[ApiController]
[Route("api/huntingpermit")]
public class HuntingPermitController : ControllerBase
{
    private readonly HuntingPermit_db _db;
    private readonly ResourceOwnership_db _ownership;
    private readonly ILogger<HuntingPermitController> _logger;

    public HuntingPermitController(HuntingPermit_db db, ResourceOwnership_db ownership, ILogger<HuntingPermitController> logger)
    {
        _db = db;
        _ownership = ownership;
        _logger = logger;
    }

    /// <summary>
    /// Submits a new hunting-permit application for userRoleId, which must be one of
    /// the caller's own roles (Admins may submit for any role).
    /// </summary>
    [HttpPost("newApplication")]
    public async Task<IActionResult> NewApplication([FromBody] CreatePermitApplicationRequest request, [FromQuery] int userRoleId)
    {
        if (userRoleId <= 0) return BadRequest(new { message = "userRoleId word benodig." });
        if (!await User.OwnsUserRoleOrAdminAsync(_ownership, userRoleId)) return Forbid();
        var id = await _db.NewApplication(request, userRoleId);

        _logger.LogInformation("Permit {Id} sent to SAPS", id);

        return Ok(new HuntingPermitResponse { Id = id, Message = "Application submitted successfully" });
    }

    /// <summary>Lists every permit application in the system.</summary>
    [Authorize(Roles = "Admin")]
    [HttpGet("getApplication")]
    public async Task<IActionResult> GetApplications() => Ok(await _db.GetApplications());

    /// <summary>Lists permit applications submitted by a specific userRoleId - only that role's owner or an Admin.</summary>
    [HttpGet("getApplicationsByUserRole/{userRoleId}")]
    public async Task<IActionResult> GetApplicationsByUserRole(int userRoleId)
    {
        if (!await User.OwnsUserRoleOrAdminAsync(_ownership, userRoleId)) return Forbid();
        return Ok(await _db.GetApplicationsByUserRole(userRoleId));
    }

    /// <summary>Sets an application's approval status (e.g. Approved/Rejected).</summary>
    [Authorize(Roles = "Admin")]
    [HttpPost("setApproval/{id}")]
    public async Task<IActionResult> SetApproval(int id, [FromBody] string status)
    {
        return (await _db.SetApproval(id, status)) ? Ok() : NotFound();
    }

    /// <summary>
    /// Accepts a SAPS 520 firearm-permit form and logs a simulated transmission to
    /// the SAPS Central Firearms Register - there is no real SAPS integration behind
    /// this; the console/logger output stands in for an actual submission receipt,
    /// per the project's enterprise-simulation spec. The form is still persisted via
    /// SaveSapsForm regardless of the simulated transmission result.
    /// </summary>
    /// <summary>
    /// Minimum a SAPS 520 needs before it may be filed: the applicant's surname and full
    /// names and exactly one of SA ID / passport number. Accepts both the WebApp's field
    /// names (surname, idNum, ...) and the SapsForm model's (permitSurname, permitIdNum, ...).
    /// </summary>
    private static string? ValidateSaps520(System.Text.Json.JsonElement form)
    {
        string Field(params string[] names)
        {
            foreach (var n in names)
                foreach (var p in form.EnumerateObject())
                    if (string.Equals(p.Name, n, StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == System.Text.Json.JsonValueKind.String)
                        return p.Value.GetString()?.Trim() ?? string.Empty;
            return string.Empty;
        }
        if (form.ValueKind != System.Text.Json.JsonValueKind.Object) return "Die SAPS 520-vorm is leeg.";
        if (Field("surname", "permitSurname").Length == 0 || Field("fullNames", "permitFullNames").Length == 0)
            return "Van en volle name is verpligtend op die SAPS 520.";
        bool hasId = Field("idNum", "permitIdNum").Length > 0, hasPassport = Field("passportNum", "permitPassportNum").Length > 0;
        if (hasId == hasPassport) return "Vul asseblief SLEGS SA ID-nommer OF paspoortnommer in (een van die twee).";
        return null;
    }

    [HttpPost("submitSaps520")]
    public async Task<IActionResult> SubmitSaps520([FromBody] System.Text.Json.JsonElement formElement)
    {
        string rawJson = formElement.GetRawText();
        int userId = User.GetUserId() ?? 0;
        if (formElement.TryGetProperty("userId", out var uProp))
            userId = uProp.GetInt32();
        if (!User.IsSelfOrAdmin(userId))
            return StatusCode(403, new { message = "Geen toegang om 'n SAPS 520 namens hierdie gebruiker in te dien nie." });
        var problem = ValidateSaps520(formElement);
        if (problem != null) return BadRequest(new { message = problem });

        string name = "Hunter";
        if (formElement.TryGetProperty("fullNames", out var fn) && formElement.TryGetProperty("surname", out var sn))
            name = $"{fn.GetString()} {sn.GetString()}";

        // Terminal simulation logging as required by enterprise spec
        Console.WriteLine("================================================================================");
        Console.WriteLine($"[SAPS 520 TRANSMISSION SIMULATOR]");
        Console.WriteLine($"Simulating submission of SAPS 520 Permit for Hunter ID #{userId} ({name})");
        Console.WriteLine($"Status: Transmitted to SAPS Central Firearms Register (CFR) & Outfitter.");
        Console.WriteLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine("================================================================================");

        _logger.LogInformation("[SAPS 520 SIMULATION] Form for {Name} submitted to CFR and Outfitter", name);

        int id = await _db.SaveSapsForm(userId, rawJson);
        return Ok(new { id, message = "SAPS 520 submitted and logged to terminal successfully" });
    }
}
