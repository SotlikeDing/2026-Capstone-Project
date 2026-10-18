using Microsoft.AspNetCore.Mvc;
using WebAPI.Database;

namespace WebAPI.Controllers;

/// <summary>
/// Unauthenticated, read-only pricing/business settings - any visitor deciding
/// whether to advertise, subscribe to a role, or book needs to see the current price
/// before signing in. Admin-only operational settings (rate limits, JWT lifetime,
/// etc.) are NOT exposed here - those stay behind AdminController.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class SettingsController : ControllerBase
{
    private readonly SystemSettings_db _settingsDb;

    public SettingsController(SystemSettings_db settingsDb)
    {
        _settingsDb = settingsDb;
    }

    /// <summary>
    /// What the website needs before anyone signs in: maintenance mode (show the maintenance page)
    /// and whether new registrations are open (admin flags).
    /// </summary>
    [HttpGet("status")]
    public async Task<IActionResult> GetSiteStatus([FromServices] WebAPI.Services.IFeatureFlags flags)
    {
        return Ok(new
        {
            maintenance = await flags.IsEnabledAsync(WebAPI.Services.FeatureFlagKeys.MaintenanceMode, false),
            registrationsOpen = await flags.IsEnabledAsync(WebAPI.Services.FeatureFlagKeys.NewUserRegistrations, true),
            message = WebAPI.Services.MaintenanceModeMiddleware.Message
        });
    }

    /// <summary>Returns every system setting flagged IsPublic, e.g. current ad/subscription pricing.</summary>
    [HttpGet("public")]
    public async Task<IActionResult> GetPublicSettings()
    {
        return Ok(await _settingsDb.GetPublicAsync());
    }
}
