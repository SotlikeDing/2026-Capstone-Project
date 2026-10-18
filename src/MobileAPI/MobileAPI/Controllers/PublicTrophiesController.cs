using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MobileApi.Database;
using MobileApi.Models;
using MobileApi.Services;

namespace MobileApi.Controllers;

[ApiController]
[Route("api/public-trophies")]
[AllowAnonymous]
public class PublicTrophiesController : ControllerBase
{
    private readonly IPublicTrophiesRepository _db;
    private readonly IRowlandWardEvaluationService _evaluationService;
    private readonly IRowlandWardCatalogueService _catalogue;

    public PublicTrophiesController(
        IPublicTrophiesRepository db,
        IRowlandWardEvaluationService evaluationService,
        IRowlandWardCatalogueService catalogue)
    {
        _db = db;
        _evaluationService = evaluationService;
        _catalogue = catalogue;
    }

    [HttpGet("legacy-species")]
    public async Task<IActionResult> GetLegacySpecies()
    {
        try
        {
            return Ok(await _db.GetPublicLegacySpeciesAsync());
        }
        catch
        {
            return StatusCode(500, new
            {
                message = "Kon nie ongekategoriseerde spesies laai nie."
            });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetPublicTrophies()
    {
        try
        {
            var trophies = await _db.GetPublicTrophiesAsync();
            AddPublicRowlandWardInformation(trophies);
            return Ok(trophies);
        }
        catch
        {
            return StatusCode(500, new { message = "Kon nie openbare trofeë laai nie." });
        }
    }

    [HttpGet("species")]
    public async Task<IActionResult> GetSpecies()
    {
        try
        {
            return Ok(await _db.GetPublicSpeciesAsync());
        }
        catch
        {
            return StatusCode(500, new { message = "Kon nie openbare spesies laai nie." });
        }
    }

    [HttpGet("firearm-types")]
    public async Task<IActionResult> GetFirearmTypes()
    {
        try
        {
            return Ok(await _db.GetPublicFirearmTypesAsync());
        }
        catch
        {
            return StatusCode(500, new { message = "Kon nie openbare vuurwapentipes laai nie." });
        }
    }

    [HttpGet("leaderboard")]
    public async Task<IActionResult> GetLeaderboard(
        [FromQuery] string? categoryId,
        [FromQuery] string? classId,
        [FromQuery] bool legacyOnly = false,
        [FromQuery] string? legacySpecies = null,
        [FromQuery] string? firearmType = null)
    {
        var normalizedCategoryId = Normalize(categoryId);
        var normalizedClassId = Normalize(classId);
        var normalizedLegacySpecies = Normalize(legacySpecies);
        var normalizedFirearmType = Normalize(firearmType);

        if (legacyOnly &&
            (normalizedCategoryId is not null || normalizedClassId is not null))
        {
            return BadRequest(new
            {
                message = "Kies óf 'n Rowland Ward-kategorie óf ongekategoriseerde trofeë."
            });
        }

        if (!legacyOnly && normalizedLegacySpecies is not null)
        {
            return BadRequest(new
            {
                message = "Kies eers ongekategoriseerde trofeë voordat jy 'n historiese spesie kies."
            });
        }

        IReadOnlyCollection<string>? classIds = null;

        if (!legacyOnly && normalizedClassId is not null)
        {
            var recordClass = _catalogue.GetRecordClass(normalizedClassId);
            if (recordClass is null)
            {
                return BadRequest(new
                {
                    message = "Die gekose Rowland Ward-spesie is nie geldig nie."
                });
            }

            if (normalizedCategoryId is not null &&
                !string.Equals(
                    recordClass.CategoryId,
                    normalizedCategoryId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new
                {
                    message = "Die gekose spesie behoort nie aan hierdie kategorie nie."
                });
            }

            classIds = [recordClass.Id];
        }
        else if (!legacyOnly && normalizedCategoryId is not null)
        {
            var category = _catalogue.GetCategories().FirstOrDefault(candidate =>
                string.Equals(
                    candidate.Id,
                    normalizedCategoryId,
                    StringComparison.OrdinalIgnoreCase));

            if (category is null)
            {
                return BadRequest(new
                {
                    message = "Die gekose Rowland Ward-kategorie is nie geldig nie."
                });
            }

            classIds = _catalogue
                .GetRecordClasses(category.Id)
                .Select(recordClass => recordClass.Id)
                .ToArray();
        }

        try
        {
            var trophies = await _db.GetLeaderboardAsync(
                classIds,
                legacyOnly,
                normalizedLegacySpecies,
                normalizedFirearmType
            );
            AddPublicRowlandWardInformation(trophies);
            return Ok(trophies);
        }
        catch
        {
            return StatusCode(500, new { message = "Kon nie trofeeranglys laai nie." });
        }
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [HttpGet("{trophyId:int}")]
    public async Task<IActionResult> GetPublicTrophyById(int trophyId)
    {
        try
        {
            var trophy = await _db.GetPublicTrophyByIdAsync(trophyId);
            if (trophy is not null)
            {
                AddPublicRowlandWardInformation([trophy]);
            }

            return trophy is null
                ? NotFound(new { message = "Die openbare trofee bestaan nie." })
                : Ok(trophy);
        }
        catch
        {
            return StatusCode(500, new { message = "Kon nie openbare trofee laai nie." });
        }
    }

    private void AddPublicRowlandWardInformation<T>(IEnumerable<T> trophies)
        where T : PublicTrophySummary
    {
        foreach (var trophy in trophies)
        {
            if (trophy.StoredRowlandWardEvaluation is not null)
            {
                trophy.RowlandWard = _evaluationService.ToPublicEvaluation(
                    trophy.StoredRowlandWardEvaluation
                );
            }
        }
    }
}

