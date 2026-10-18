using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebAPI.Database;
using WebAPI.Models;

namespace WebAPI.Controllers;

/// <summary>
/// The hunters on a booking. The main hunter (who books and pays) adds the other
/// hunters once the booking is confirmed, up to the booking's hunter count; added
/// hunters then see the booking (never its price or payment) under "trips I'm
/// joining". The rules themselves live in BookingCompanions_db.
/// </summary>
[Authorize]
[ApiController]
[Route("api/bookings")]
public class BookingCompanionsController : ControllerBase
{
    private readonly BookingCompanions_db _db;
    private readonly Services.IEmailService? _emailService;
    private readonly ResourceOwnership_db? _ownership;
    private readonly IConfiguration? _configuration;

    public BookingCompanionsController(BookingCompanions_db db, Services.IEmailService? emailService = null,
        ResourceOwnership_db? ownership = null, IConfiguration? configuration = null)
    {
        _db = db;
        _emailService = emailService;
        _ownership = ownership;
        _configuration = configuration;
    }

    /// <summary>The booking's hunters - for the main hunter, its added hunters, the booking's PJ/PH/VG/farmer, or an Admin.</summary>
    [HttpGet("{bookingId:int}/hunters")]
    public async Task<IActionResult> GetHunters(int bookingId)
    {
        var callerId = User.GetUserId();
        if (callerId == null) return Unauthorized();

        var (roster, callerIsOnBooking) = await _db.GetBookingHunters(bookingId, callerId.Value, User.IsAdmin());
        if (roster == null) return User.IsAdmin() ? NotFound() : Forbid();
        if (callerIsOnBooking || User.IsAdmin()) return Ok(roster);

        bool isStaff = _ownership != null && (await _ownership.GetBookingParticipantUserIds(bookingId)).Contains(callerId.Value);
        return isStaff ? Ok(roster) : Forbid();
    }

    /// <summary>The main hunter adds a hunter by name + email (only on a confirmed booking with an open slot).</summary>
    [HttpPost("{bookingId:int}/hunters")]
    public async Task<IActionResult> AddHunter(int bookingId, [FromBody] AddBookingHunterRequest request)
    {
        var callerId = User.GetUserId();
        if (callerId == null) return Unauthorized();

        string name = request?.Name?.Trim() ?? "";
        string email = request?.Email?.Trim() ?? "";
        if (name.Length == 0 || name.Length > 200)
            return BadRequest(new { message = "Voer die jagter se naam in (hoogstens 200 karakters)." });
        if (email.Length > 255 || !System.Text.RegularExpressions.Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
            return BadRequest(new { message = "Voer 'n geldige e-posadres in." });

        var result = await _db.AddCompanion(bookingId, callerId.Value, User.IsAdmin(), name, email);
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
                return User.IsAdmin() ? NotFound() : Forbid();
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
        var callerId = User.GetUserId();
        if (callerId == null) return Unauthorized();

        return await _db.RemoveCompanion(bookingId, companionId, callerId.Value, User.IsAdmin()) switch
        {
            RemoveCompanionOutcome.Removed => Ok(new { message = "Jagter verwyder." }),
            RemoveCompanionOutcome.NotFound => NotFound(),
            RemoveCompanionOutcome.Locked => Conflict(new { message = "Die jag is reeds afgehandel; die jagterslys kan nie meer verander nie." }),
            _ => Forbid(),
        };
    }

    /// <summary>Bookings the caller joins as an added hunter (links any of their pending invites first). No money fields.</summary>
    [HttpGet("companionBookings")]
    public async Task<IActionResult> GetCompanionBookings()
    {
        var callerId = User.GetUserId();
        if (callerId == null) return Unauthorized();
        return Ok(await _db.GetCompanionBookings(callerId.Value));
    }

    /// <summary>Best-effort: a mail problem must not undo the add.</summary>
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
