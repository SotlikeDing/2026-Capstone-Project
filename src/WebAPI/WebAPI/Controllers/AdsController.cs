using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebAPI.Database;
using WebAPI.Models;

namespace WebAPI.Controllers;

/// <summary>
/// Advertiser (AP) role: ad CRUD, payment recording, and impression/click tracking.
/// Most write endpoints accept an optional userId query param as a fallback for
/// unauthenticated/legacy callers, but prefer the caller's own JWT identity, and let
/// an admin act on another user's ads via that same param - see GetCallerInfo.
/// </summary>
[Authorize]
[ApiController]
[Route("api/[controller]")]
public class AdsController : ControllerBase
{
    private readonly Ads_db _db;
    public AdsController(Ads_db db) { _db = db; }

    /// <summary>Resolves the calling user's id (from JWT claims, if present) and whether they hold the Admin role.</summary>
    private (int? UserId, bool IsAdmin) GetCallerInfo()
    {
        var userIdClaim = User.FindFirst("sub")?.Value
            ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("userId")?.Value;

        int? uid = int.TryParse(userIdClaim, out var parsed) && parsed > 0 ? parsed : null;

        bool isAdmin = User.IsInRole("Admin")
            || User.IsInRole("Administrator")
            || User.IsInRole("Administrateur")
            || string.Equals(User.FindFirst("role")?.Value, "Admin", StringComparison.OrdinalIgnoreCase)
            || string.Equals(User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value, "Admin", StringComparison.OrdinalIgnoreCase);

        return (uid, isAdmin);
    }

    /// <summary>Paginated ad listing, optionally filtered by status, owning user, or AP profile.</summary>
    [AllowAnonymous]
    [HttpGet("getAds")]
    public async Task<IActionResult> GetAds([FromQuery] int page = 1, [FromQuery] int pageSize = 100, [FromQuery] string? status = null, [FromQuery] int? userId = null, [FromQuery] int? apId = null)
    {
        var ads = await _db.GetAds(page, pageSize, status, userId, apId);
        return Ok(ads);
    }

    /// <summary>Fetches a single ad by id.</summary>
    [AllowAnonymous]
    [HttpGet("getAd/{id}")]
    public async Task<IActionResult> GetAd(int id)
    {
        var ad = await _db.GetAd(id);
        return ad == null ? NotFound() : Ok(ad);
    }

    /// <summary>Creates a new ad owned by the caller's AP role (403 without one). An Admin may name another advertiser in request.UserId.</summary>
    [HttpPost("createAd")]
    public async Task<IActionResult> CreateAd([FromBody] CreateAdRequest request)
    {
        try
        {
            var (callerUserId, isAdmin) = GetCallerInfo();
            int userId = isAdmin && request.UserId is > 0 ? request.UserId.Value : callerUserId ?? 0;

            if (userId <= 0)
            {
                return BadRequest(new { message = "'n Geldige gebruiker met die AP rol word benodig om 'n advertensie te skep." });
            }

            // A new ad starts unpaid; it goes live only through a payment (api/payments).
            if (!isAdmin) StripPaymentFields(request);

            var id = await _db.CreateAd(userId, request);
            if (id <= 0)
            {
                return StatusCode(500, new { message = "Kon nie die advertensie in die databasis stoor nie." });
            }

            return Ok(new { Id = id, AdvertisementId = id, Success = true });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { message = ex.Message });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AdsController.CreateAd Error] {ex}");
            return StatusCode(500, new { message = ex.Message, details = ex.ToString() });
        }
    }

    /// <summary>Updates an ad's details. An admin may target another user's ad via the userId query param; a non-admin is always scoped to their own.</summary>
    [HttpPost("updateAd/{id}")]
    public async Task<IActionResult> UpdateAd(int id, [FromBody] UpdateAdRequest request, [FromQuery] int? userId = null)
    {
        try
        {
            var (callerUserId, isAdmin) = GetCallerInfo();
            int? effectiveUserId = isAdmin ? (userId ?? callerUserId) : (callerUserId ?? userId);

            if (!isAdmin)
            {
                // The advertiser edits content and may pause/resume a paid ad; what was paid,
                // until when, and the "Aktief" state itself come only from a payment.
                StripPaymentFields(request);
                if (request.Status != null)
                {
                    bool pausing = string.Equals(request.Status, "Onaktief", StringComparison.OrdinalIgnoreCase);
                    bool resuming = string.Equals(request.Status, "Aktief", StringComparison.OrdinalIgnoreCase);
                    if (resuming)
                    {
                        var current = await _db.GetAd(id);
                        bool paidAndRunning = current != null && !string.IsNullOrWhiteSpace(current.PaymentRef)
                                              && current.ExpiryDate.HasValue && current.ExpiryDate.Value > DateTime.Now;
                        if (!paidAndRunning) request.Status = null;
                    }
                    else if (!pausing)
                    {
                        request.Status = null;
                    }
                }
            }

            var ok = await _db.UpdateAd(id, request, effectiveUserId);
            return ok ? Ok(new { Success = true }) : NotFound(new { message = $"Advertensie {id} nie gevind of magtiging geweier." });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AdsController.UpdateAd Error] {ex}");
            return StatusCode(500, new { message = ex.Message, details = ex.ToString() });
        }
    }

    /// <summary>Admin-only: records a payment against an ad by hand (e.g. an EFT). Advertisers pay through api/payments.</summary>
    [Authorize(Roles = "Admin")]
    [HttpPost("recordPayment/{id}")]
    public async Task<IActionResult> RecordPayment(int id, [FromBody] RecordPaymentRequest request, [FromQuery] int? userId = null)
    {
        try
        {
            var (callerUserId, isAdmin) = GetCallerInfo();
            int? effectiveUserId = isAdmin ? (request.UserId ?? userId ?? callerUserId) : (callerUserId ?? request.UserId ?? userId);

            var ok = await _db.RecordPayment(id, request, effectiveUserId);
            return ok ? Ok(new { Success = true }) : NotFound(new { message = $"Advertensie {id} nie gevind of magtiging geweier." });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AdsController.RecordPayment Error] {ex}");
            return StatusCode(500, new { message = ex.Message, details = ex.ToString() });
        }
    }

    /// <summary>Lists payment records, optionally filtered by ad. Non-admin callers are always scoped to their own payments regardless of the userId query param.</summary>
    [HttpGet("getPayments")]
    public async Task<IActionResult> GetPayments([FromQuery] int? adId = null, [FromQuery] int? userId = null)
    {
        try
        {
            var (callerUserId, isAdmin) = GetCallerInfo();
            int? effectiveUserId = userId;

            // Non-admin authenticated callers are strictly scoped to their own payment records
            if (callerUserId.HasValue && !isAdmin)
            {
                effectiveUserId = callerUserId.Value;
            }

            var payments = await _db.GetPayments(adId, effectiveUserId);
            return Ok(payments);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AdsController.GetPayments Error] {ex}");
            return StatusCode(500, new { message = ex.Message });
        }
    }

    /// <summary>Increments an ad's impression (view) counter - called by the frontend each time the ad is rendered.</summary>
    [AllowAnonymous]
    [HttpPost("recordImpression/{id}")]
    public async Task<IActionResult> RecordImpression(int id, [FromQuery] int? viewerUserId = null)
    {
        var ok = await _db.RecordImpression(id, viewerUserId);
        return ok ? Ok(new { Success = true }) : NotFound();
    }

    /// <summary>Increments an ad's click counter.</summary>
    [AllowAnonymous]
    [HttpPost("recordClick/{id}")]
    public async Task<IActionResult> RecordClick(int id, [FromQuery] int? viewerUserId = null)
    {
        var ok = await _db.RecordClick(id, viewerUserId);
        return ok ? Ok(new { Success = true }) : NotFound();
    }

    /// <summary>Deletes an ad. Same admin-vs-owner scoping as UpdateAd.</summary>
    [HttpDelete("deleteAd/{id}")]
    public async Task<IActionResult> DeleteAd(int id, [FromQuery] int? userId = null)
    {
        var (callerUserId, isAdmin) = GetCallerInfo();
        int? effectiveUserId = isAdmin ? (userId ?? callerUserId) : (callerUserId ?? userId);

        return (await _db.DeleteAd(id, effectiveUserId)) ? Ok(new { Success = true }) : NotFound();
    }

    /// <summary>Clears what only a payment may set: status, expiry, price and payment details.</summary>
    private static void StripPaymentFields(CreateAdRequest request)
    {
        request.Status = null;
        request.ExpiryDate = null;
        request.Price = null;
        request.PaymentRef = null;
        request.LastPaymentMonths = null;
        request.LastPaymentAmount = null;
        request.LastPaymentDate = null;
    }

    /// <summary>Clears what only a payment may set (status is handled by the caller).</summary>
    private static void StripPaymentFields(UpdateAdRequest request)
    {
        request.ExpiryDate = null;
        request.Price = null;
        request.PaymentRef = null;
        request.LastPaymentMonths = null;
        request.LastPaymentAmount = null;
        request.LastPaymentDate = null;
    }
}
