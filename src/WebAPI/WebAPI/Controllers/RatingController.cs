using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WebAPI.Database;
using WebAPI.Models;
using WebAPI.Services;

namespace WebAPI.Controllers;

/// <summary>
/// Post-trip star ratings between hunters and their PH/VG (and vice versa): submitting
/// a rating, reading aggregate stats/recent reviews for a profile, and letting a
/// ratee dispute a rating they consider unfair. Every write path re-derives the
/// caller's identity from their JWT rather than trusting request-body user ids.
/// </summary>
[Authorize]
[ApiController]
[Route("api/[controller]")]
public class RatingController : ControllerBase
{
    private readonly Rating_db _db;
    private readonly Bookings_db _bookingsDb;

    private static readonly HashSet<string> ValidRateeRoles = new(StringComparer.OrdinalIgnoreCase) { "HUNTER", "PH", "VG" };

    public RatingController(Rating_db db, Bookings_db bookingsDb)
    {
        _db = db;
        _bookingsDb = bookingsDb;
    }

    /// <summary>
    /// Records a post-trip rating. Requires a valid bearer token; the caller must be
    /// rating as themselves (RaterUserId must match their own JWT claim - nobody can
    /// submit a rating on someone else's behalf), and both the rater and ratee must
    /// have actually been participants, in the stated capacity, on a Completed
    /// booking. Called by whatever client collects the rating (mobile app after a
    /// completed trip); the webapp only reads via the endpoints below.
    /// </summary>
    [HttpPost("setRoleRating")]
    [Authorize(AuthenticationSchemes = InternalServiceAuthenticationHandler.BearerOrInternal)]
    [EnableRateLimiting("RatingPolicy")]
    public async Task<IActionResult> SetRoleRating([FromBody] SetRatingRequest request)
    {
        if (request.Stars < 1 || request.Stars > 5)
            return BadRequest(new { message = "Gradering moet tussen 1 en 5 sterre wees." });
        if (request.BookingId <= 0 || request.RaterUserId <= 0 || request.RateeUserId <= 0)
            return BadRequest(new { message = "Ongeldige bespreking- of gebruikersverwysing." });
        if (request.RaterUserId == request.RateeUserId)
            return BadRequest(new { message = "'n Gebruiker kan nie homself gradeer nie." });
        if (string.IsNullOrWhiteSpace(request.RateeRoleCode) || !ValidRateeRoles.Contains(request.RateeRoleCode))
            return BadRequest(new { message = "Rolkonteks vir die gradering moet HUNTER, PH of VG wees." });

        // A caller can only submit a rating as themselves - the identity comes from
        // the validated JWT, never trusted from the request body.
        int callerId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;
        if (callerId <= 0 || callerId != request.RaterUserId)
            return StatusCode(403, new { message = "U kan slegs 'n gradering as uself indien." });

        var participants = await _bookingsDb.GetBookingParticipants(request.BookingId);
        if (!participants.BookingExists)
            return NotFound(new { message = "Bespreking nie gevind nie." });
        if (!participants.IsRateable(DateTime.UtcNow))
            return BadRequest(new { message = "Slegs voltooide besprekings kan gegradeer word." });

        bool raterWasOnBooking = request.RaterUserId == participants.HunterUserId
            || request.RaterUserId == participants.PhUserId
            || request.RaterUserId == participants.VgUserId;
        if (!raterWasOnBooking)
            return StatusCode(403, new { message = "U was nie deel van hierdie ekspedisie nie." });

        int? expectedRateeUserId = request.RateeRoleCode.ToUpperInvariant() switch
        {
            "HUNTER" => participants.HunterUserId,
            "PH" => participants.PhUserId,
            "VG" => participants.VgUserId,
            _ => null
        };
        if (expectedRateeUserId == null || expectedRateeUserId != request.RateeUserId)
            return BadRequest(new { message = "Die aangeduide persoon was nie in daardie rol op hierdie bespreking nie." });

        int ratingId = await _db.SetRoleRating(request);
        return ratingId > 0 ? Ok(new { success = true }) : BadRequest(new { message = "Kon nie gradering stoor nie." });
    }

    /// <summary>
    /// The other participants on a booking that the caller can rate, each with the
    /// caller's current rating of them. Drives the "rate your team" panel on the
    /// website booking pages and (via MobileAPI) the mobile app. Only participants of
    /// the booking may read it.
    /// </summary>
    [HttpGet("booking/{bookingId:int}/team")]
    [Authorize(AuthenticationSchemes = InternalServiceAuthenticationHandler.BearerOrInternal)]
    [EnableRateLimiting("RatingTeamReadPolicy")]
    public async Task<IActionResult> GetBookingTeam(int bookingId)
    {
        int callerId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;
        if (callerId <= 0) return Unauthorized();

        var participants = await _bookingsDb.GetBookingParticipants(bookingId);
        if (!participants.BookingExists)
            return NotFound(new { message = "Bespreking nie gevind nie." });
        if (callerId != participants.HunterUserId && callerId != participants.PhUserId && callerId != participants.VgUserId)
            return StatusCode(403, new { message = "U was nie deel van hierdie ekspedisie nie." });

        var own = (await _db.GetOwnRatingsForBooking(bookingId, callerId)).ToDictionary(x => x.RateeUserId);
        var result = new BookingTeamRatingDto { BookingId = bookingId, CallerUserId = callerId, CanRate = participants.IsRateable(DateTime.UtcNow) };
        if (!result.CanRate)
            result.Reason = "U kan u span gradeer sodra die ekspedisie verby of voltooi is.";

        void Add(int? userId, string? name, string roleCode)
        {
            if (userId is not { } uid || uid == callerId || result.Members.Any(m => m.UserId == uid)) return;
            own.TryGetValue(uid, out var mine);
            result.Members.Add(new BookingTeamMemberDto
            {
                UserId = uid,
                Name = string.IsNullOrWhiteSpace(name) ? $"Gebruiker #{uid}" : name.Trim(),
                RoleCode = roleCode,
                MyStars = mine?.Stars,
                MyComment = mine?.Comment
            });
        }
        Add(participants.HunterUserId, participants.HunterName, "HUNTER");
        Add(participants.PhUserId, participants.PhName, "PH");
        Add(participants.VgUserId, participants.VgName, "VG");
        return Ok(result);
    }

    /// <summary>Average stars, rating count and completed-trip count for a profile page/dashboard card.</summary>
    [AllowAnonymous]
    [HttpGet("profile-stats/{userId}")]
    [EnableRateLimiting("RatingReadPolicy")]
    public async Task<IActionResult> GetProfileStats(int userId, [FromQuery] string? role = null)
    {
        var summary = await _db.GetRatingSummary(userId, role);
        var completedTrips = await _bookingsDb.GetCompletedTripCount(userId);

        return Ok(new RatingProfileStatsDto
        {
            AverageStars = summary.AverageStars,
            RatingCount = summary.RatingCount,
            CompletedTrips = completedTrips
        });
    }

    /// <summary>Recent ratings with comments for a reviews list.</summary>
    [AllowAnonymous]
    [HttpGet("recent/{userId}")]
    [EnableRateLimiting("RatingReadPolicy")]
    public async Task<IActionResult> GetRecentRatings(int userId, [FromQuery] int take = 5, [FromQuery] string? role = null)
    {
        if (take <= 0 || take > 50) take = 5;
        var list = await _db.GetRecentRatings(userId, take, role);
        return Ok(list);
    }

    /// <summary>
    /// The person who was rated formally disputing a rating they received (e.g. it's
    /// retaliatory or factually wrong but doesn't trip the keyword moderation filter).
    /// Only the ratee themselves can dispute their own rating - identity comes from
    /// the validated JWT, same as submission. An admin reviews it via
    /// AdminController's rating-disputes endpoints.
    /// </summary>
    [HttpPost("{ratingId}/dispute")]
    [Authorize]
    [EnableRateLimiting("RatingPolicy")]
    public async Task<IActionResult> DisputeRating(int ratingId, [FromBody] CreateDisputeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            return BadRequest(new { message = "'n Rede is verpligtend om 'n gradering te betwis." });

        int rateeUserId = await _db.GetRatingRateeUserId(ratingId);
        if (rateeUserId == 0)
            return NotFound(new { message = "Gradering nie gevind nie." });

        int callerId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;
        if (callerId <= 0 || callerId != rateeUserId)
            return StatusCode(403, new { message = "U kan slegs graderings betwis wat oor uself gegee is." });

        bool ok = await _db.CreateDispute(ratingId, callerId, request.Reason);
        if (ok) return Ok(new { success = true });

        // The only reason CreateDispute can fail once ownership is confirmed is that
        // this dispute was already resolved - that decision is final by design.
        string? existingStatus = await _db.GetDisputeStatus(ratingId);
        string message = string.Equals(existingStatus, "Upheld", StringComparison.OrdinalIgnoreCase) || string.Equals(existingStatus, "Denied", StringComparison.OrdinalIgnoreCase)
            ? "Hierdie gradering se dispuut is reeds hersien en afgehandel."
            : "Kon nie dispuut indien nie.";
        return BadRequest(new { message });
    }
}
