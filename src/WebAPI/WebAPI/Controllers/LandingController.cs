using Microsoft.AspNetCore.Mvc;
using WebAPI.Database;
using WebAPI.Models;

namespace WebAPI.Controllers;

/// <summary>
/// Public content for the marketing landing page: site contact details, a small
/// sample of featured packages, and the top-rated-hunters leaderboard.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class LandingController : ControllerBase
{
    private readonly Landing_db _db;
    public LandingController(Landing_db db) { _db = db; }

    /// <summary>Site-wide contact details shown in the landing page footer.</summary>
    [HttpGet("getContact")]
    public async Task<IActionResult> GetContact()
    {
        var contact = await _db.GetContact();
        return contact == null ? NotFound() : Ok(contact);
    }

    /// <summary>A small featured subset of hunting packages for the landing page hero/preview section.</summary>
    [HttpGet("getPackages")]
    public async Task<IActionResult> GetPackages() => Ok(await _db.GetPackages());

    /// <summary>Top-rated hunters/roles, for the landing page's leaderboard widget.</summary>
    [HttpGet("getLeaderboard")]
    public async Task<IActionResult> GetLeaderboard() => Ok(await _db.GetLeaderboard());
}
