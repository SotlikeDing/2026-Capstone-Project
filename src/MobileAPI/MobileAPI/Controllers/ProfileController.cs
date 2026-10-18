using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MobileApi.Database;
using MobileApi.Models.RowlandWard;

namespace MobileApi.Controllers;

[ApiController]
[Route("api/profile")]
[Authorize]
public class ProfileController : ControllerBase
{
    private readonly IUserProfilePreferencesRepository _preferences;

    public ProfileController(IUserProfilePreferencesRepository preferences)
    {
        _preferences = preferences;
    }

    [HttpGet("rowland-ward-preference")]
    public async Task<IActionResult> GetRowlandWardPreference()
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized(new { message = "Jou aanmeldbesonderhede kon nie bevestig word nie." });
        }

        try
        {
            var enabled = await _preferences.GetRowlandWardEnabledAsync(userId);

            return enabled is null
                ? NotFound(new { message = "Jou profiel kon nie gevind word nie." })
                : Ok(new RowlandWardPreferenceResponse { RowlandWardEnabled = enabled.Value });
        }
        catch
        {
            return StatusCode(500, new { message = "Die Rowland Ward-instelling kon nie gelaai word nie." });
        }
    }

    [HttpPut("rowland-ward-preference")]
    public async Task<IActionResult> UpdateRowlandWardPreference(
        [FromBody] UpdateRowlandWardPreferenceRequest? request)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized(new { message = "Jou aanmeldbesonderhede kon nie bevestig word nie." });
        }

        if (request?.RowlandWardEnabled is null)
        {
            return BadRequest(new { message = "Kies of die Rowland Ward-funksie aan of af moet wees." });
        }

        try
        {
            var updated = await _preferences.UpdateRowlandWardEnabledAsync(
                userId,
                request.RowlandWardEnabled.Value
            );

            return updated
                ? Ok(new RowlandWardPreferenceResponse
                {
                    RowlandWardEnabled = request.RowlandWardEnabled.Value
                })
                : NotFound(new { message = "Jou profiel kon nie gevind word nie." });
        }
        catch
        {
            return StatusCode(500, new { message = "Die Rowland Ward-instelling kon nie gestoor word nie." });
        }
    }

    private bool TryGetCurrentUserId(out int userId)
    {
        var claimValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(claimValue, out userId);
    }
}
