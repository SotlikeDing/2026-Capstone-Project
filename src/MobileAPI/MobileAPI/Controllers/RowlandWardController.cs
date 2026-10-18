using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MobileApi.Database;
using MobileApi.Models.RowlandWard;
using MobileApi.Services;

namespace MobileApi.Controllers;

[ApiController]
[Route("api/rowland-ward")]
[Authorize]
[RequireMobileCapability(MobileApi.Models.MobileCapabilityNames.ViewTrophyRoom)]
public sealed class RowlandWardController : ControllerBase
{
    private readonly IRowlandWardCatalogueService _catalogue;
    private readonly IRowlandWardSpeciesResolver _resolver;
    private readonly IRowlandWardEvaluationService _evaluationService;
    private readonly IUserProfilePreferencesRepository _preferences;

    public RowlandWardController(
        IRowlandWardCatalogueService catalogue,
        IRowlandWardSpeciesResolver resolver,
        IRowlandWardEvaluationService evaluationService,
        IUserProfilePreferencesRepository preferences)
    {
        _catalogue = catalogue;
        _resolver = resolver;
        _evaluationService = evaluationService;
        _preferences = preferences;
    }

    [HttpGet("categories")]
    public IActionResult GetCategories() => Ok(_catalogue.GetCategories());

    [HttpGet("classes")]
    public IActionResult GetClasses([FromQuery] string? categoryId = null) =>
        Ok(_catalogue.GetRecordClasses(categoryId));

    [HttpGet("classes/{classId}")]
    public IActionResult GetClass(string classId)
    {
        var recordClass = _catalogue.GetRecordClass(classId);
        return recordClass is null
            ? NotFound(new { message = "Geen goedgekeurde Rowland Ward-rekordklas is gevind nie." })
            : Ok(recordClass);
    }

    [HttpGet("search")]
    public IActionResult Search(
        [FromQuery] string query,
        [FromQuery] string? categoryId = null,
        [FromQuery] int limit = 20)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return BadRequest(new { message = "Tik asseblief 'n wildspesie om te soek." });
        }

        return Ok(_resolver.Search(query, categoryId, limit));
    }

    [HttpGet("match")]
    public IActionResult Match(
        [FromQuery] string input,
        [FromQuery] int limit = 5)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return BadRequest(new { message = "Tik asseblief 'n wildspesie." });
        }

        return Ok(_resolver.Match(input, limit));
    }

    [HttpGet("classes/{classId}/measurement-requirements")]
    public IActionResult GetMeasurementRequirements(string classId)
    {
        var requirements = _catalogue.GetMeasurementRequirements(classId);
        return requirements is null
            ? NotFound(new { message = "Geen Rowland Ward-metings is vir hierdie rekordklas beskikbaar nie." })
            : Ok(requirements);
    }

    [HttpPost("evaluate")]
    public async Task<IActionResult> Evaluate([FromBody] RowlandWardEvaluationRequest request)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized(new { message = "Jou aanmeldbesonderhede kon nie bevestig word nie." });
        }

        try
        {
            if (await _preferences.GetRowlandWardEnabledAsync(userId) != true)
            {
                return BadRequest(new
                {
                    message = "Skakel eers die Rowland Ward-funksie in jou profiel aan."
                });
            }

            return Ok(_evaluationService.Evaluate(request));
        }
        catch (RowlandWardEvaluationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
        catch
        {
            return StatusCode(500, new
            {
                message = "Die Rowland Ward-telling kon nie bereken word nie."
            });
        }
    }

    private bool TryGetCurrentUserId(out int userId)
    {
        var claimValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(claimValue, out userId);
    }
}
