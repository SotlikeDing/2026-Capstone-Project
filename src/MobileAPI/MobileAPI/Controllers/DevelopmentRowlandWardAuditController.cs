using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MobileApi.Services;

namespace MobileApi.Controllers;

[ApiController]
[Authorize]
[Route("api/development/rowland-ward")]
public sealed class DevelopmentRowlandWardAuditController : ControllerBase
{
    private readonly IHostEnvironment _environment;
    private readonly ILegacyTrophySpeciesAuditService _auditService;

    public DevelopmentRowlandWardAuditController(
        IHostEnvironment environment,
        ILegacyTrophySpeciesAuditService auditService)
    {
        _environment = environment;
        _auditService = auditService;
    }

    [HttpGet("legacy-species-audit")]
    public async Task<IActionResult> GetLegacySpeciesAudit()
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound(new
            {
                message = "Die ontwikkelingsoudit is nie in hierdie omgewing beskikbaar nie."
            });
        }

        try
        {
            return Ok(await _auditService.AuditAsync());
        }
        catch
        {
            return StatusCode(500, new
            {
                message = "Die bestaande trofeespesies kon nie geoudit word nie."
            });
        }
    }
}
