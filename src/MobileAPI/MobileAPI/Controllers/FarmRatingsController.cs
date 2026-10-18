using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MobileApi.Database;
using MobileApi.Models;

namespace MobileApi.Controllers;

[ApiController]
[Route("api/farm-ratings")]
[Authorize]
public class FarmRatingsController : ControllerBase
{
    private const int MaximumDescriptionLength = 2000;
    private readonly FarmRatings_db _db;

    public FarmRatingsController(FarmRatings_db db)
    {
        _db = db;
    }

    [HttpPost("bookings/{bookingId:int}")]
    public async Task<IActionResult> SetFarmRating(int bookingId, [FromBody] SetFarmRatingRequest request)
    {
        if (bookingId <= 0)
        {
            return BadRequest(new { message = "Ongeldige bespreking." });
        }

        if (request.Stars < 0 || request.Stars > 5)
        {
            return BadRequest(new { message = "Die stergradering moet tussen 0 en 5 wees." });
        }

        string? description = string.IsNullOrWhiteSpace(request.Description)
            ? null
            : request.Description.Trim();
        if (description?.Length > MaximumDescriptionLength)
        {
            return BadRequest(new { message = $"Die beskrywing mag nie langer as {MaximumDescriptionLength} karakters wees nie." });
        }

        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out int callerUserId) || callerUserId <= 0)
        {
            return Unauthorized();
        }

        var result = await _db.SetFarmRating(bookingId, callerUserId, request.Stars, description);
        return result.Status switch
        {
            SaveFarmRatingStatus.Success => Ok(new
            {
                success = true,
                ratingId = result.RatingId,
                farmId = result.FarmId
            }),
            SaveFarmRatingStatus.BookingNotFound => NotFound(new { message = "Bespreking nie gevind nie." }),
            SaveFarmRatingStatus.Forbidden => StatusCode(403, new { message = "U kan slegs die plaas van u eie bespreking gradeer." }),
            SaveFarmRatingStatus.FarmNotFound => BadRequest(new { message = "Hierdie bespreking is nie aan 'n plaas gekoppel nie." }),
            SaveFarmRatingStatus.BookingNotEligible => BadRequest(new { message = "Die plaas kan eers gegradeer word nadat die jag plaasgevind het of voltooi is." }),
            _ => BadRequest(new { message = "Kon nie die plaasgradering stoor nie." })
        };
    }

    [HttpGet("farms/{farmId:int}/summary")]
    public async Task<IActionResult> GetFarmRatingSummary(int farmId)
    {
        if (farmId <= 0)
        {
            return BadRequest(new { message = "Ongeldige plaas." });
        }

        var summary = await _db.GetFarmRatingSummary(farmId);
        return summary == null ? NotFound(new { message = "Plaas nie gevind nie." }) : Ok(summary);
    }
}
