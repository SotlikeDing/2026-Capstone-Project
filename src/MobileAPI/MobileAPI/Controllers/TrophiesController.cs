using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MobileApi.Database;
using MobileApi.Models;
using MobileApi.Models.RowlandWard;
using MobileApi.Services;
using MobileApi.Validation;

namespace MobileApi.Controllers;

[ApiController]
[Route("api/trophies")]
[Authorize]
public class TrophiesController : ControllerBase
{
    private readonly ITrophiesRepository _db;
    private readonly IUserProfilePreferencesRepository _preferences;
    private readonly IRowlandWardEvaluationService _evaluationService;
    private readonly ITrophyPhotoStorage _photoStorage;
    private readonly IMobileAccessService _mobileAccess;

    public TrophiesController(
        ITrophiesRepository db,
        IUserProfilePreferencesRepository preferences,
        IRowlandWardEvaluationService evaluationService,
        ITrophyPhotoStorage photoStorage,
        IMobileAccessService mobileAccess)
    {
        _db = db;
        _preferences = preferences;
        _evaluationService = evaluationService;
        _photoStorage = photoStorage;
        _mobileAccess = mobileAccess;
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] TrophySaveRequest request)
    {
        if (!await IsHunterAsync()) return Forbid();
        return Conflict(new
        {
            message = "Nuwe trofeë moet vanuit 'n voltooide bespreking ingedien en deur die toegewysde PH goedgekeur word."
        });
    }

    [HttpGet]
    public async Task<IActionResult> GetMine()
    {
        if (!await IsHunterAsync()) return Forbid();
        if (!TryGetCurrentUserId(out var ownerUserId))
        {
            return Unauthorized(new { message = "Jou aanmeldbesonderhede kon nie bevestig word nie." });
        }

        try
        {
            var trophies = await _db.GetForOwnerAsync(ownerUserId);
            foreach (var trophy in trophies)
            {
                AttachCurrentWorldRecord(trophy);
            }

            return Ok(trophies);
        }
        catch
        {
            return StatusCode(500, new { message = "Jou trofeë kon nie gelaai word nie." });
        }
    }

    [HttpGet("{trophyId:int}")]
    public async Task<IActionResult> GetById(int trophyId)
    {
        if (!await IsHunterAsync()) return Forbid();
        if (!TryGetCurrentUserId(out var ownerUserId))
        {
            return Unauthorized(new { message = "Jou aanmeldbesonderhede kon nie bevestig word nie." });
        }

        try
        {
            var trophy = await _db.GetByIdForOwnerAsync(trophyId, ownerUserId);
            AttachCurrentWorldRecord(trophy);

            return trophy is null
                ? NotFound(new { message = "Die trofee bestaan nie of behoort nie aan jou nie." })
                : Ok(trophy);
        }
        catch
        {
            return StatusCode(500, new { message = "Die trofee kon nie gelaai word nie." });
        }
    }

    [HttpPut("{trophyId:int}")]
    public async Task<IActionResult> Update(int trophyId, [FromBody] TrophySaveRequest request)
    {
        if (!await IsHunterAsync()) return Forbid();
        if (!TryGetCurrentUserId(out var ownerUserId))
        {
            return Unauthorized(new { message = "Jou aanmeldbesonderhede kon nie bevestig word nie." });
        }

        try
        {
            var existing = await _db.GetByIdForOwnerAsync(trophyId, ownerUserId);
            if (existing is null)
            {
                return NotFound(new { message = "Die trofee bestaan nie of behoort nie aan jou nie." });
            }

            if (!IsUpdatedPhotoPathValid(request.PhotoPath, existing.PhotoPath, ownerUserId))
            {
                return BadRequest(new
                {
                    message = "Laai die nuwe trofeefoto deur die app op voordat jy dit stoor."
                });
            }

            var (gateError, evaluation) = await EvaluateRequestedRowlandWardAsync(
                ownerUserId,
                request
            );
            if (gateError is not null)
            {
                return gateError;
            }

            if (TrophySaveRequestValidator.GetError(request) is { } error)
            {
                return BadRequest(new { message = error });
            }

            var updated = await _db.UpdateForOwnerAsync(
                trophyId,
                ownerUserId,
                request,
                evaluation
            );
            if (!updated)
            {
                return NotFound(new { message = "Die trofee bestaan nie of behoort nie aan jou nie." });
            }

            if (!string.Equals(existing.PhotoPath, request.PhotoPath, StringComparison.Ordinal))
            {
                await _photoStorage.DeleteIfOwnedAsync(existing.PhotoPath, ownerUserId);
            }

            var trophy = await _db.GetByIdForOwnerAsync(trophyId, ownerUserId);
            AttachCurrentWorldRecord(trophy);
            return Ok(new
            {
                success = true,
                message = "Trofee is suksesvol bygewerk.",
                trophy
            });
        }
        catch (RowlandWardEvaluationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
        catch
        {
            return StatusCode(500, new { message = "Die trofee kon nie bygewerk word nie." });
        }
    }

    [HttpDelete("{trophyId:int}")]
    public async Task<IActionResult> Delete(int trophyId)
    {
        if (!await IsHunterAsync()) return Forbid();
        if (!TryGetCurrentUserId(out var ownerUserId))
        {
            return Unauthorized(new { message = "Jou aanmeldbesonderhede kon nie bevestig word nie." });
        }

        try
        {
            var existing = await _db.GetByIdForOwnerAsync(trophyId, ownerUserId);
            if (existing is null)
            {
                return NotFound(new { message = "Die trofee bestaan nie of behoort nie aan jou nie." });
            }

            var deleted = await _db.DeleteForOwnerAsync(trophyId, ownerUserId);

            if (deleted)
            {
                await _photoStorage.DeleteIfOwnedAsync(existing.PhotoPath, ownerUserId);
            }

            return deleted
                ? Ok(new { success = true, message = "Trofee is suksesvol verwyder." })
                : NotFound(new { message = "Die trofee bestaan nie of behoort nie aan jou nie." });
        }
        catch
        {
            return StatusCode(500, new { message = "Die trofee kon nie verwyder word nie." });
        }
    }

    private async Task<(IActionResult? Error, RowlandWardEvaluationSnapshot? Evaluation)>
        EvaluateRequestedRowlandWardAsync(int ownerUserId, TrophySaveRequest request)
    {
        if (request.RowlandWard is null)
        {
            return (null, null);
        }

        var enabled = await _preferences.GetRowlandWardEnabledAsync(ownerUserId);
        if (enabled != true)
        {
            return (
                BadRequest(new
                {
                    message = "Skakel eers die Rowland Ward-funksie in jou profiel aan."
                }),
                null
            );
        }

        var evaluation = _evaluationService.Evaluate(request.RowlandWard);
        if (evaluation.QualificationState ==
            RowlandWardQualificationStates.RequiredMeasurementsMissing)
        {
            return (
                BadRequest(new
                {
                    message = "Vul asseblief al die vereiste Rowland Ward-metings in.",
                    missingMeasurementFieldIds = evaluation.MissingMeasurementFieldIds
                }),
                null
            );
        }

        request.Species = evaluation.DisplayNameAf;
        return (null, evaluation);
    }

    private void AttachCurrentWorldRecord(Trophy? trophy)
    {
        if (trophy?.RowlandWard is not null)
        {
            _evaluationService.AttachCurrentWorldRecord(trophy.RowlandWard);
        }
    }

    private bool IsNewPhotoPathValid(string? photoPath, int ownerUserId)
    {
        return string.IsNullOrWhiteSpace(photoPath) ||
            _photoStorage.IsOwnedPath(photoPath, ownerUserId);
    }

    private bool IsUpdatedPhotoPathValid(
        string? requestedPhotoPath,
        string? existingPhotoPath,
        int ownerUserId)
    {
        return string.IsNullOrWhiteSpace(requestedPhotoPath) ||
            string.Equals(requestedPhotoPath, existingPhotoPath, StringComparison.Ordinal) ||
            _photoStorage.IsOwnedPath(requestedPhotoPath, ownerUserId);
    }

    private bool TryGetCurrentUserId(out int userId)
    {
        var claimValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(claimValue, out userId);
    }

    private async Task<bool> IsHunterAsync()
    {
        var caller = await _mobileAccess.ResolveAsync(User, HttpContext.RequestAborted);
        return caller.Can(MobileCapabilityNames.ViewTrophyRoom) && caller.Role == MobileRole.Hunter;
    }
}
