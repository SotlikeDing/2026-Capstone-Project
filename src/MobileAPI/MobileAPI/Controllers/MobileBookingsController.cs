using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MobileApi.Database;
using MobileApi.Models;
using MobileApi.Services;

namespace MobileApi.Controllers;

[ApiController]
[Route("api/bookings")]
[Authorize]
public class MobileBookingsController : ControllerBase
{
    private readonly MobileBookings_db _db;
    private readonly MobileBookingCompanions_db? _companions;
    private readonly IEmailService? _emailService;
    private readonly IConfiguration? _configuration;
    private readonly IMobileAccessService? _mobileAccess;

    public MobileBookingsController(MobileBookings_db db, MobileBookingCompanions_db? companions = null,
        IEmailService? emailService = null, IConfiguration? configuration = null, IMobileAccessService? mobileAccess = null)
    {
        _db = db;
        _companions = companions;
        _emailService = emailService;
        _configuration = configuration;
        _mobileAccess = mobileAccess;
    }

    /// <summary>Bookings for the explicitly selected Hunter, PH or VG role.</summary>
    [HttpGet]
    public async Task<IActionResult> GetMyBookings(CancellationToken cancellationToken)
    {
        if (_mobileAccess == null) return StatusCode(503);
        var caller = await _mobileAccess.ResolveAsync(User, cancellationToken);
        if (!caller.IsSupported) return Forbid();
        return Ok(await _mobileAccess.GetBookingsAsync(caller, cancellationToken));
    }

    /// <summary>The hunter's own bookings plus the trips a main hunter added them to.</summary>
    [HttpGet("user/{userRoleId}")]
    public async Task<IActionResult> GetHunterBookings(int userRoleId)
    {
        if (_mobileAccess != null)
        {
            var caller = await _mobileAccess.ResolveAsync(User, HttpContext.RequestAborted);
            if (caller.Role != MobileRole.Hunter || caller.UserRoleId != userRoleId) return Forbid();
        }
        if (!UserHasUserRole(userRoleId))
        {
            return Forbid();
        }

        // Link any invites for this hunter's (verified) email first, so a trip they were
        // invited to before they had an account shows up on their first login.
        var userId = CallerUserId();
        if (_companions != null && userId != null)
        {
            try
            {
                await _companions.ClaimPendingInvites(userId.Value);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BOOKING INVITES] Could not link invites for user {userId}: {ex.Message}");
            }
        }

        var bookings = await _db.GetHunterBookings(userRoleId);
        return Ok(bookings);
    }

    /// <summary>The booking's hunters - only for its main hunter and the hunters on it.</summary>
    [HttpGet("{bookingId:int}/hunters")]
    public async Task<IActionResult> GetHunters(int bookingId)
    {
        var userId = CallerUserId();
        if (userId == null || _companions == null) return Unauthorized();

        var (roster, callerIsOnBooking) = await _companions.GetBookingHunters(bookingId, userId.Value, isAdmin: false);
        return roster != null && callerIsOnBooking ? Ok(roster) : Forbid();
    }

    /// <summary>The main hunter adds a hunter by name + email (confirmed booking, open slot, hunters only).</summary>
    [HttpPost("{bookingId:int}/hunters")]
    public async Task<IActionResult> AddHunter(int bookingId, [FromBody] AddBookingHunterRequest request)
    {
        var userId = CallerUserId();
        if (userId == null || _companions == null) return Unauthorized();

        string name = request?.Name?.Trim() ?? "";
        string email = request?.Email?.Trim() ?? "";
        if (name.Length == 0 || name.Length > 200)
            return BadRequest(new { message = "Voer die jagter se naam in (hoogstens 200 karakters)." });
        if (email.Length > 255 || !System.Text.RegularExpressions.Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
            return BadRequest(new { message = "Voer 'n geldige e-posadres in." });

        var result = await _companions.AddCompanion(bookingId, userId.Value, isAdmin: false, name, email);
        switch (result.Outcome)
        {
            case AddCompanionOutcome.Linked:
            case AddCompanionOutcome.Invited:
                await SendAddedEmailAsync(result);
                return Ok(new
                {
                    hunter = result.Hunter,
                    message = result.Outcome == AddCompanionOutcome.Linked
                        ? $"{result.Hunter!.Name} is by die bespreking gevoeg."
                        : $"'n Uitnodiging is na {result.Hunter!.Email} gestuur. Sodra hulle as jagter registreer en hul e-pos verifieer, word hulle outomaties gekoppel.",
                });
            case AddCompanionOutcome.BookingNotFound:
            case AddCompanionOutcome.NotMainHunter:
                return Forbid();
            case AddCompanionOutcome.NotConfirmed:
                return Conflict(new { message = "Jagters kan eers bygevoeg word sodra die bespreking bevestig is." });
            case AddCompanionOutcome.Full:
                return Conflict(new { message = "Al die jagterplekke op hierdie bespreking is reeds gevul." });
            case AddCompanionOutcome.IsMainHunter:
                return Conflict(new { message = "Jy is reeds die hoofjagter op hierdie bespreking." });
            case AddCompanionOutcome.NotAHunter:
                return Conflict(new { message = "Hierdie persoon is nie as jagter op MikPunt geregistreer nie. Slegs jagters kan bygevoeg word." });
            case AddCompanionOutcome.AlreadyAdded:
                return Conflict(new { message = "Hierdie persoon is reeds by die bespreking gevoeg." });
            default:
                return StatusCode(500);
        }
    }

    /// <summary>The main hunter removes an added hunter, or an added hunter leaves the trip.</summary>
    [HttpDelete("{bookingId:int}/hunters/{companionId:int}")]
    public async Task<IActionResult> RemoveHunter(int bookingId, int companionId)
    {
        var userId = CallerUserId();
        if (userId == null || _companions == null) return Unauthorized();

        return await _companions.RemoveCompanion(bookingId, companionId, userId.Value, isAdmin: false) switch
        {
            RemoveCompanionOutcome.Removed => Ok(new { message = "Jagter verwyder." }),
            RemoveCompanionOutcome.NotFound => NotFound(),
            RemoveCompanionOutcome.Locked => Conflict(new { message = "Die jag is reeds afgehandel; die jagterslys kan nie meer verander nie." }),
            _ => Forbid(),
        };
    }

    private bool UserHasUserRole(int userRoleId)
    {
        var claimValue = User.FindFirstValue("userRoleId");
        return int.TryParse(claimValue, out var currentUserRoleId) && currentUserRoleId == userRoleId;
    }

    private int? CallerUserId()
    {
        var claimValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(claimValue, out var id) && id > 0 ? id : null;
    }

    /// <summary>Same email as the WebAPI's BookingCompanionsController. Best-effort.</summary>
    private async Task SendAddedEmailAsync(AddCompanionResult result)
    {
        if (_emailService == null || result.Hunter?.Email == null) return;
        try
        {
            string baseUrl = (_configuration?["WebAppBaseUrl"] ?? "https://localhost:7285").TrimEnd('/');
            string who = WebUtility.HtmlEncode(result.MainHunterName ?? "'n Jagter");
            string trip = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(result.BookingTitle) ? "'n jagekspedisie" : result.BookingTitle);
            string name = WebUtility.HtmlEncode(result.Hunter.Name);
            bool invited = result.Outcome == AddCompanionOutcome.Invited;
            string link = invited
                ? $"{baseUrl}/registreer?email={Uri.EscapeDataString(result.Hunter.Email)}"
                : $"{baseUrl}/jagter/besprekings";
            string action = invited
                ? "Registreer asseblief as <strong>jagter</strong> op MikPunt met hierdie e-posadres en verifieer dit. Die bespreking verskyn dan outomaties by jou besprekings, op die webwerf en in die Jagbestuur-app."
                : "Jy kan die besonderhede sien onder <em>Reise waarby ek aansluit</em> op die webwerf en in die Jagbestuur-app.";

            string body = $@"
                <div style='font-family: Arial, sans-serif; padding: 20px; color: #333; max-width: 600px; margin: 0 auto; border: 1px solid #e0e0e0; border-radius: 8px;'>
                    <h2 style='color: #6C5037;'>Jy is by 'n jag gevoeg</h2>
                    <p>Hallo {name},</p>
                    <p>{who} het jou as jagter by <strong>{trip}</strong> gevoeg.</p>
                    <p>{action}</p>
                    <p style='text-align: center; margin: 24px 0;'>
                        <a href='{WebUtility.HtmlEncode(link)}' style='background-color: #6C5037; color: #fff; padding: 12px 24px; border-radius: 6px; text-decoration: none;'>{(invited ? "Registreer by MikPunt" : "Bekyk die bespreking")}</a>
                    </p>
                    <p style='color: #777; font-size: 13px;'>Die hoofjagter is verantwoordelik vir betaling; jy sien geen prys- of betaalinligting nie.</p>
                </div>";
            await _emailService.SendEmailAsync(result.Hunter.Email, "MikPunt - Jy is uitgenooi vir 'n jagekspedisie", body);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[EMAIL] Could not send booking-hunter email: {ex.Message}");
        }
    }
}
