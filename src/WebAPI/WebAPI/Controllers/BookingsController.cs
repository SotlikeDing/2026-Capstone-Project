using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebAPI.Database;
using WebAPI.Models;

namespace WebAPI.Controllers;

/// <summary>
/// The full booking lifecycle: creation (with document-verification gating),
/// browsing by role (hunter/PH/VG/farm), the PJ approve/reject/staff-assignment
/// flow, PH/VG accept-or-decline of a staff assignment (which finalizes/approves
/// the booking once everyone's accepted and emails the hunter a voucher), payment
/// status, and post-booking updates/audit history.
/// </summary>
[Authorize]
[ApiController]
[Route("api/bookings")]
public class BookingsController : ControllerBase
{
    private readonly Bookings_db _db;
    private readonly Services.IEmailService? _emailService;
    private readonly UserDocuments_db? _documentsDb;
    private readonly ResourceOwnership_db? _ownership;

    public BookingsController(Bookings_db db, Services.IEmailService? emailService = null, UserDocuments_db? documentsDb = null, ResourceOwnership_db? ownership = null)
    {
        _db = db;
        _emailService = emailService;
        _documentsDb = documentsDb;
        _ownership = ownership;
    }

    /// <summary>The calling user's own id, from their validated JWT.</summary>
    private bool TryGetCallerUserId(out int callerUserId)
    {
        callerUserId = 0;
        return int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out callerUserId) && callerUserId > 0;
    }

    /// <summary>The booking's PJ or the owner of its farm - the people who decide on it (not the hunter who asked).</summary>
    private async Task<bool> CallerManagesBookingAsync(BookingResponse booking, int callerUserId)
    {
        if (booking.PjUserId.HasValue && booking.PjUserId.Value == callerUserId) return true;
        return booking.FarmId.HasValue && _ownership != null
            && (await _ownership.GetFarmOwnerUserIds(booking.FarmId.Value)).Contains(callerUserId);
    }

    /// <summary>Statuses the booking's PJ/farmer may set directly. Paid is never set by hand - only a verified payment marks it.</summary>
    private static readonly HashSet<string> ManagerStatuses = new(StringComparer.OrdinalIgnoreCase) { "Approved", "Rejected", "Cancelled", "Completed" };

    /// <summary>Statuses a hunter may still cancel from: anything before payment.</summary>
    private static readonly HashSet<string> HunterCancellableStatuses = new(StringComparer.OrdinalIgnoreCase) { "Pending", "PendingStaffAcceptance", "Approved" };

    /// <summary>Everyone taking part in the booking (hunter, PH, VG, PJ, farmer) or an Admin. Fails closed when the ownership service isn't available.</summary>
    private Task<bool> CallerIsBookingParticipantAsync(int bookingId)
        => User.IsOwnerOrAdminAsync(() => _ownership?.GetBookingParticipantUserIds(bookingId) ?? Task.FromResult(new HashSet<int>()));

    /// <summary>Lists bookings for a PJ's packages (id = PJ userId) - only that PJ or an Admin.</summary>
    [HttpGet("getBookings")]
    public async Task<IActionResult> GetBookings([FromQuery] int id)
        => User.IsSelfOrAdmin(id) ? Ok(await _db.GetBookings(id)) : Forbid();

    /// <summary>Lists all pending booking requests on the platform - Admin only.</summary>
    [Authorize(Roles = "Admin")]
    [HttpGet("getBookingRequest")]
    public async Task<IActionResult> GetBookingRequests() => Ok(await _db.GetBookingRequests());

    /// <summary>Lists bookings a PH (id = userId) is or was assigned to - only that PH or an Admin.</summary>
    [HttpGet("getPhBookings")]
    public async Task<IActionResult> GetPhBookings([FromQuery] int id)
        => User.IsSelfOrAdmin(id) ? Ok(await _db.GetPhBookings(id)) : Forbid();

    /// <summary>Lists bookings a VG (id = userId) is or was assigned to - only that VG or an Admin.</summary>
    [HttpGet("getVgBookings")]
    public async Task<IActionResult> GetVgBookings([FromQuery] int id)
        => User.IsSelfOrAdmin(id) ? Ok(await _db.GetVgBookings(id)) : Forbid();

    /// <summary>Lists a hunter's (id = userId) own bookings - only that hunter or an Admin.</summary>
    [HttpGet("getHunterBookings")]
    public async Task<IActionResult> GetHunterBookings([FromQuery] int id)
        => User.IsSelfOrAdmin(id) ? Ok(await _db.GetHunterBookings(id)) : Forbid();

    /// <summary>Lists bookings against a farmer's (id = userId) farms - only that farmer or an Admin.</summary>
    [HttpGet("getFarmBookings")]
    public async Task<IActionResult> GetFarmBookings([FromQuery] int id)
        => User.IsSelfOrAdmin(id) ? Ok(await _db.GetFarmBookings(id)) : Forbid();

    /// <summary>Fetches full details for a single booking - only its participants or an Admin.</summary>
    [HttpGet("getSpecificBooking/{id}")]
    public async Task<IActionResult> GetSpecificBooking(int id)
    {
        if (!await CallerIsBookingParticipantAsync(id)) return Forbid();
        var b = await _db.GetSpecificBooking(id);
        return b == null ? NotFound() : Ok(b);
    }

    [Authorize]
    [HttpDelete("deleteBooking/{bookingId}/{userId}")]
    public async Task<IActionResult> DeleteBooking(int bookingId, int userId)
    {
        // The route's userId is client-supplied and therefore spoofable - the caller's
        // own authenticated identity is what actually gets checked against ownership.
        if (!TryGetCallerUserId(out int callerUserId))
            return Unauthorized();

        return await _db.DeleteBooking(bookingId, callerUserId) switch
        {
            CancelBookingResult.Cancelled => Ok(new { message = "Bespreking gekanselleer." }),
            CancelBookingResult.NotCancellable => Conflict(new { message = "Hierdie bespreking is reeds goedgekeur of betaal. Kontak asseblief die uitruster (PJ) om dit te kanselleer." }),
            _ => Forbid()
        };
    }

    /// <summary>Change-history log for a booking (approvals, staff reassignments, price corrections, etc.) - only its participants or an Admin.</summary>
    [HttpGet("getAuditLogs/{id}")]
    public async Task<IActionResult> GetAuditLogs(int id)
    {
        if (!await CallerIsBookingParticipantAsync(id)) return Forbid();
        return Ok(await _db.GetBookingAuditLogs(id));
    }

    /// <summary>Creates a new booking request for the calling hunter against a package.</summary>
    [Authorize]
    [HttpPost("create")]
    public async Task<IActionResult> CreateBooking([FromBody] CreateBookingRequest request)
    {
        if (!TryGetCallerUserId(out int callerUserId))
            return Unauthorized();
        if (request == null || request.PakketId <= 0)
            return BadRequest(new { message = "Kies asseblief 'n pakket om te bespreek." });
        var today = DateTime.UtcNow.AddHours(2).Date; // South African date
        if (request.StartDate.Date < today)
            return BadRequest(new { message = "Die aankomsdatum kan nie in die verlede wees nie." });
        if (request.EndDate.Date < request.StartDate.Date)
            return BadRequest(new { message = "Die vertrekdatum moet op of na die aankomsdatum wees." });
        if (request.HuntGuests < 1 || request.NonHuntGuests < 0)
            return BadRequest(new { message = "Daar moet ten minste een jagter wees." });

        // hunterId is always the caller's own identity, never the client-supplied
        // request.HunterId - otherwise any authenticated user could create a booking
        // "as" an arbitrary other hunter.
        int hunterId = callerUserId;
        int packageId = request.PakketId;
        decimal totalAmount = 0;

        // Defense-in-depth: the package should already be hidden from public browse
        // (Package_db) once its farm's farmer or its PJ organiser has an outstanding
        // required document, but a stale/cached client view must never be able to
        // slip a booking through anyway - so both sides are re-checked here too.
        if (_documentsDb != null)
        {
            var farmerUserRoleId = await _db.GetFarmerUserRoleIdForPackage(packageId);
            if (farmerUserRoleId.HasValue && !await _documentsDb.AreRequiredDocumentsApproved(farmerUserRoleId.Value))
            {
                return BadRequest(new { message = "Hierdie plaas se dokumente is nog nie deur 'n administrateur geverifieer nie. Bespreking kan nie geskep word nie." });
            }

            var pjUserRoleId = await _db.GetPjUserRoleIdForPackage(packageId);
            if (pjUserRoleId.HasValue && !await _documentsDb.AreRequiredDocumentsApproved(pjUserRoleId.Value))
            {
                return BadRequest(new { message = "Hierdie pakket se uitruster se dokumente is nog nie deur 'n administrateur geverifieer nie. Bespreking kan nie geskep word nie." });
            }
        }

        var id = await _db.CreateBooking(request, hunterId, packageId, totalAmount);
        return Ok(new { Id = id });
    }

    /// <summary>
    /// Sets a booking's status directly (legacy path - prefer ApproveWithStaff for the normal PJ
    /// approval flow). The booking's PJ or farmer may approve, reject, cancel or complete it; its
    /// hunter may only cancel it before paying; nobody but an Admin can set it to Paid here.
    /// </summary>
    [Authorize]
    [HttpPost("approveBooking/{id}")]
    public async Task<IActionResult> ApproveBooking(int id, [FromBody] string status)
    {
        if (!TryGetCallerUserId(out int callerUserId))
            return Unauthorized();
        if (string.IsNullOrWhiteSpace(status))
            return BadRequest(new { message = "Geen status gegee nie." });

        if (!User.IsInRole("Admin"))
        {
            var booking = await _db.GetSpecificBooking(id);
            if (booking == null) return NotFound();
            if (await CallerManagesBookingAsync(booking, callerUserId))
            {
                if (!ManagerStatuses.Contains(status))
                    return StatusCode(403, new { message = "Hierdie status kan nie met die hand gestel word nie." });
            }
            else if (booking.UserId == callerUserId)
            {
                // The hunter can only cancel their own request, and only before it is paid.
                if (!string.Equals(status, "Cancelled", StringComparison.OrdinalIgnoreCase))
                    return StatusCode(403, new { message = "U kan u bespreking slegs kanselleer." });
                if (!HunterCancellableStatuses.Contains(booking.Status))
                    return Conflict(new { message = "'n Betaalde of afgehandelde bespreking kan nie meer gekanselleer word nie. Kontak asseblief ondersteuning." });
            }
            else
            {
                return StatusCode(403, new { message = "U het nie toegang tot hierdie bespreking nie." });
            }
        }

        return (await _db.ApproveBooking(id, status)) ? Ok() : NotFound();
    }

    /// <summary>Rejects a booking request with an optional reason/notes - the booking's PJ, its farmer or an Admin (a hunter cancels instead).</summary>
    [Authorize]
    [HttpPost("rejectBooking/{id}")]
    public async Task<IActionResult> RejectBooking(int id, [FromBody] RejectBookingRequest? request = null)
    {
        if (!TryGetCallerUserId(out int callerUserId))
            return Unauthorized();
        if (!User.IsInRole("Admin"))
        {
            var booking = await _db.GetSpecificBooking(id);
            if (booking == null) return NotFound();
            if (!await CallerManagesBookingAsync(booking, callerUserId))
                return StatusCode(403, new { message = "Slegs die uitruster of boer kan 'n bespreking afkeur." });
        }

        var ok = await _db.RejectBookingWithReason(id, request?.ReasonDescription ?? request?.ReasonCode, request?.Notes);
        return ok ? Ok(new { success = true }) : NotFound();
    }

    /// <summary>
    /// PJ assigns a PH (required) and optionally a VG to a booking and confirms
    /// pricing. This alone does not approve the booking - see StaffRespond, which
    /// finalizes approval once every assigned role has accepted.
    /// </summary>
    [Authorize]
    [HttpPost("approveWithStaff/{id}")]
    public async Task<IActionResult> ApproveWithStaff(int id, [FromBody] ApproveWithStaffRequest request)
    {
        if (!TryGetCallerUserId(out int callerUserId))
            return Unauthorized();
        if (!User.IsInRole("PJ") && !User.IsInRole("Admin"))
            return StatusCode(403, new { message = "Slegs 'n Uitruster (PJ) kan personeel aan 'n bespreking toewys." });

        if (request.PhUserId <= 0)
            return BadRequest(new { message = "PH Gids is verpligtend vir goedkeuring." });
        if (!await _db.IsValidStaffAssignee(request.PhUserId, "PH"))
            return BadRequest(new { message = "Die gekose gebruiker is nie 'n geregistreerde PH gids nie." });
        if (request.VgUserId.HasValue && request.VgUserId.Value > 0 && !await _db.IsValidStaffAssignee(request.VgUserId.Value, "VG"))
            return BadRequest(new { message = "Die gekose gebruiker is nie 'n geregistreerde videograaf nie." });

        var success = await _db.ApproveBookingWithStaff(id, request.PhUserId, request.VgUserId, request.ConfirmedTotalPrice, request.ConfirmedAccommodationPrice, request.PriceBreakdownJson);
        if (!success)
            return NotFound();

        // Note: no voucher email here anymore - assigning staff no longer approves the
        // booking outright. The voucher fires from staffRespond once every assigned
        // role has actually accepted (see below).
        return Ok(new { success = true });
    }

    /// <summary>PH or VG accepts/declines the assignment the PJ gave them. Requires the
    /// caller to actually be the currently-pending assignee for that role.</summary>
    [Authorize]
    [HttpPost("staffRespond/{id}")]
    public async Task<IActionResult> StaffRespond(int id, [FromBody] RespondStaffAssignmentRequest request)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out int callerUserId) || callerUserId <= 0)
            return Unauthorized();
        if (string.IsNullOrWhiteSpace(request.Role))
            return BadRequest(new { message = "Rol is verpligtend." });
        string roleNorm = request.Role.Trim().ToUpperInvariant();
        if (roleNorm != "PH" && roleNorm != "VG")
            return BadRequest(new { message = "Rol moet PH of VG wees." });

        var (result, bookingNowApproved) = await _db.RespondToStaffAssignment(id, roleNorm, callerUserId, request.Accept, request.DeclineReason);

        switch (result)
        {
            case Bookings_db.StaffResponseResult.NotFound:
                return NotFound(new { message = "Geen hangende toewysing vir hierdie rol gevind nie." });
            case Bookings_db.StaffResponseResult.Forbidden:
                return StatusCode(403, new { message = "U is nie die toegewese persoon vir hierdie rol nie." });
            case Bookings_db.StaffResponseResult.InvalidRole:
                return BadRequest(new { message = "Rol moet PH of VG wees." });
        }

        // Dispatch proof / voucher to hunter's email now that the booking is actually approved
        if (bookingNowApproved && _emailService != null)
        {
            try
            {
                var booking = await _db.GetSpecificBooking(id);
                if (booking != null && !string.IsNullOrWhiteSpace(booking.HunterEmail))
                {
                    string subject = $"MikPunt - Amptelike Ekspedisie-Bewys & Goedkeuring #{booking.PaymentReference}";
                    string emailHtml = GenerateVoucherEmailHtml(booking);
                    await _emailService.SendEmailAsync(booking.HunterEmail, subject, emailHtml, true);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BookingsController] Failed to send voucher email: {ex.Message}");
            }
        }

        return Ok(new { success = true, bookingApproved = bookingNowApproved });
    }

    /// <summary>Builds the HTML "official expedition proof" voucher email sent to the hunter once a booking is fully approved.</summary>
    private static string GenerateVoucherEmailHtml(BookingResponse b)
    {
        var pb = !string.IsNullOrWhiteSpace(b.PriceBreakdownJson) 
            ? System.Text.Json.JsonSerializer.Deserialize<BookingPriceBreakdown>(b.PriceBreakdownJson) 
            : null;

        string speciesRows = "";
        if (pb?.ConfirmedSpecies != null && pb.ConfirmedSpecies.Count > 0)
        {
            foreach (var sp in pb.ConfirmedSpecies)
            {
                string lineTotal = sp.SelectedCount > 0 ? $"${(sp.PricePerAnimal * sp.SelectedCount):N2}" : "Tarief van krag";
                string qtyText = sp.SelectedCount > 0 ? sp.SelectedCount.ToString() : "1";
                speciesRows += $@"
                    <tr style='border-bottom: 1px solid #e5e7eb;'>
                        <td style='padding: 8px 10px; font-weight: 500;'>{sp.SpeciesName}</td>
                        <td style='padding: 8px 10px; text-align: right;'>$ {sp.PricePerAnimal:N2}</td>
                        <td style='padding: 8px 10px; text-align: right;'>{qtyText}</td>
                        <td style='padding: 8px 10px; text-align: right; font-weight: bold;'>{lineTotal}</td>
                    </tr>";
            }
        }
        else if (b.BookedSpecies != null && b.BookedSpecies.Count > 0)
        {
            foreach (var sp in b.BookedSpecies)
            {
                speciesRows += $@"
                    <tr style='border-bottom: 1px solid #e5e7eb;'>
                        <td colspan='3' style='padding: 8px 10px; font-weight: 500;'>{sp}</td>
                        <td style='padding: 8px 10px; text-align: right; font-weight: bold;'>Ingesluit by kwotasie</td>
                    </tr>";
            }
        }

        int days = Math.Max(1, (b.EndDate - b.StartDate).Days);

        return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8' />
    <title>MikPunt Ekspedisie-Bewys</title>
</head>
<body style='font-family: Arial, sans-serif; background-color: #f4f5f7; margin: 0; padding: 20px; color: #1f2937;'>
    <div style='max-width: 680px; margin: 0 auto; background: #ffffff; border: 1px solid #e5e7eb; border-radius: 8px; overflow: hidden; box-shadow: 0 4px 12px rgba(0,0,0,0.05);'>
        <div style='background-color: #2b180c; color: #ffffff; padding: 20px 24px; text-align: center; border-bottom: 3px solid #d97706;'>
            <h1 style='margin: 0; font-size: 20px; color: #ffffff; letter-spacing: 1px;'>MIKPUNT JAG- &amp; EKOTOERISME PORTAAL</h1>
            <p style='margin: 4px 0 0 0; font-size: 12px; color: #fbbf24;'>Amptelike Ekspedisie-Bewys &amp; Kwotasie</p>
        </div>

        <div style='padding: 24px;'>
            <div style='display: flex; justify-content: space-between; margin-bottom: 18px; border-bottom: 2px solid #d97706; padding-bottom: 12px;'>
                <div>
                    <h2 style='margin: 0; font-size: 18px; color: #111827;'>{b.PakketName}</h2>
                    <div style='font-size: 13px; color: #6b7280; margin-top: 4px;'>Plaas: <strong>{b.FarmName}</strong> ({b.Province})</div>
                </div>
                <div style='text-align: right;'>
                    <div style='font-size: 11px; font-weight: bold; color: #9ca3af; text-transform: uppercase;'>Verwysing</div>
                    <div style='font-size: 16px; font-weight: bold; color: #d97706; font-family: monospace;'>{b.PaymentReference}</div>
                </div>
            </div>

            <div style='background-color: #ecfdf5; border: 1px solid #a7f3d0; border-radius: 6px; padding: 12px 16px; margin-bottom: 20px;'>
                <strong style='color: #065f46; font-size: 14px;'>✓ Ekspedisie Goedgekeur deur Uitruster</strong>
                <p style='margin: 4px 0 0 0; font-size: 12px; color: #047857;'>Goeiedag {b.UserName}, u besprekingsversoek is amptelik aanvaar en u PH gids is toegewys.</p>
            </div>

            <table style='width: 100%; border-collapse: collapse; margin-bottom: 20px; font-size: 13px;'>
                <tr>
                    <td style='padding: 8px 12px; background: #f9fafb; width: 50%; vertical-align: top;'>
                        <strong style='color: #92400e; display: block; margin-bottom: 4px;'>📅 Datums &amp; Duur</strong>
                        <div>Aankoms: <strong>{b.StartDate:dddd, dd MMM yyyy}</strong></div>
                        <div>Vertrek: <strong>{b.EndDate:dddd, dd MMM yyyy}</strong></div>
                        <div style='color: #6b7280;'>Tydsduur: {days} Dae</div>
                    </td>
                    <td style='padding: 8px 12px; background: #f9fafb; width: 50%; vertical-align: top;'>
                        <strong style='color: #92400e; display: block; margin-bottom: 4px;'>👥 Groepsamestelling</strong>
                        <div>Jagters: <strong>{b.HuntGuests}</strong></div>
                        <div>Nie-jagters: <strong>{b.NonHuntGuests}</strong></div>
                        <div style='color: #6b7280;'>Totaal: {b.HuntGuests + b.NonHuntGuests} Persone</div>
                    </td>
                </tr>
                <tr>
                    <td style='padding: 8px 12px; background: #f9fafb; vertical-align: top; border-top: 4px solid #ffffff;'>
                        <strong style='color: #92400e; display: block; margin-bottom: 4px;'>🎯 Toegewese PH Gids</strong>
                        <div><strong>{(!string.IsNullOrWhiteSpace(b.AssignedPhName) ? b.AssignedPhName : "Toegewese PH")}</strong></div>
                        <div style='color: #6b7280; font-size: 11px;'>Geregistreerde MikPunt PH</div>
                    </td>
                    <td style='padding: 8px 12px; background: #f9fafb; vertical-align: top; border-top: 4px solid #ffffff;'>
                        <strong style='color: #92400e; display: block; margin-bottom: 4px;'>🎥 Videograaf (VG)</strong>
                        <div><strong>{(!string.IsNullOrWhiteSpace(b.AssignedVgName) ? b.AssignedVgName : (b.WantsVideographer ? "Toegewys" : "Geen VG verlang"))}</strong></div>
                        <div style='color: #6b7280; font-size: 11px;'>{(b.WantsVideographer ? "Spesiale verfilming" : "Slegs jag")}</div>
                    </td>
                </tr>
            </table>

            <h3 style='font-size: 14px; color: #111827; border-bottom: 1px solid #e5e7eb; padding-bottom: 6px; margin-bottom: 12px;'>Finansiële Uiteensetting</h3>
            <table style='width: 100%; border-collapse: collapse; margin-bottom: 20px; font-size: 13px;'>
                <thead>
                    <tr style='background-color: #f3f4f6; color: #4b5563;'>
                        <th style='padding: 8px 10px; text-align: left;'>Beskrywing / Spesie</th>
                        <th style='padding: 8px 10px; text-align: right;'>Eenheidsprys</th>
                        <th style='padding: 8px 10px; text-align: right;'>Aantal</th>
                        <th style='padding: 8px 10px; text-align: right;'>Totaal</th>
                    </tr>
                </thead>
                <tbody>
                    {(pb != null ? $@"
                    <tr style='border-bottom: 1px solid #e5e7eb;'>
                        <td style='padding: 8px 10px;'>Akkommodasie &amp; Daggelde ({days} dae)</td>
                        <td style='padding: 8px 10px; text-align: right;'>-</td>
                        <td style='padding: 8px 10px; text-align: right;'>{b.HuntGuests + b.NonHuntGuests} gaste</td>
                        <td style='padding: 8px 10px; text-align: right; font-weight: bold;'>$ {pb.AccommodationTotal:N2}</td>
                    </tr>" : "")}
                    {speciesRows}
                </tbody>
                <tfoot>
                    <tr style='background: #fef3c7; font-size: 14px;'>
                        <td colspan='3' style='padding: 10px; font-weight: bold; color: #92400e;'>TOTALE KWOTASIE:</td>
                        <td style='padding: 10px; text-align: right; font-weight: bold; color: #92400e;'>$ {b.TotalAmount:N2} USD</td>
                    </tr>
                </tfoot>
            </table>

            <div style='background-color: #fef3c7; border-left: 4px solid #d97706; padding: 12px; font-size: 12px; color: #78350f; line-height: 1.4; margin-bottom: 20px;'>
                <strong>Regs- &amp; Veiligheidsverklaring:</strong> Hierdie dokument dien as amptelike statutêre bewys van bespreking en spantoewysing kragtens MikPunt Ekotoerisme Regulasies. Voltooi asseblief die nodige vrywaringsvorms by aankoms op die plaas en volg ten alle tye u PH se veiligheidsriglyne.
            </div>

            <p style='font-size: 12px; color: #6b7280; text-align: center; margin: 0;'>U kan u deposito betaal deur aan te meld by u MikPunt profiel onder My Besprekings.</p>
        </div>

        <div style='background: #f9fafb; border-top: 1px solid #e5e7eb; padding: 16px; text-align: center; font-size: 11px; color: #9ca3af;'>
            MikPunt Jag- &amp; Ekotoerisme Portaal &bull; Suid-Afrika &bull; info@mikpunt.co.za &bull; www.mikpunt.co.za
        </div>
    </div>
</body>
</html>";
    }

    /// <summary>
    /// Admin-only manual correction of a booking's payment status (e.g. reconciling a payment
    /// by hand). Hunters pay through api/payments; a booking is otherwise only marked Paid by a
    /// verified PayFast notification. Idempotent - see Bookings_db.PaymentStatusResult.AlreadyPaid.
    /// </summary>
    [Authorize(Roles = "Admin")]
    [HttpPost("setPaymentStatus/{id}")]
    public async Task<IActionResult> SetPaymentStatus(int id, [FromBody] string status)
    {
        var result = await _db.SetPaymentStatus(id, status);
        return result switch
        {
            Bookings_db.PaymentStatusResult.NotFound => NotFound(),
            Bookings_db.PaymentStatusResult.AlreadyPaid => Ok(new { success = true, alreadyPaid = true }),
            _ => Ok(new { success = true, alreadyPaid = false })
        };
    }

    /// <summary>Admin-only correction of a booking's details, price or status (dispute resolution) - it can change what is owed.</summary>
    [Authorize(Roles = "Admin")]
    [HttpPost("updateBooking/{id}")]
    public async Task<IActionResult> UpdateBooking(int id, [FromBody] UpdateBookingRequest request)
    {
        var result = await _db.UpdateBooking(id, request);
        return result ? Ok(new BookingsResponse { Success = true }) : NotFound();
    }
}

// Legacy response models for backward compatibility
/// <summary>Legacy booking-creation response shape.</summary>
public class CreateBookingResponse
{
    public int Id { get; set; }
}

/// <summary>Legacy generic success-response shape.</summary>
public class BookingsResponse
{
    public bool Success { get; set; }
}

