using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MobileApi.Database;
using MobileApi.Services;

namespace MobileApi.Controllers;

[ApiController]
[Route("api/trophies/photos")]
[Authorize]
public sealed class TrophyPhotosController : ControllerBase
{
    private readonly ITrophyPhotoStorage _storage;
    private readonly ITrophiesRepository _trophies;
    private readonly IMobileAccessService _mobileAccess;

    public TrophyPhotosController(
        ITrophyPhotoStorage storage,
        ITrophiesRepository trophies,
        IMobileAccessService mobileAccess)
    {
        _storage = storage;
        _trophies = trophies;
        _mobileAccess = mobileAccess;
    }

    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(TrophyPhotoStorage.MaxFileBytes + 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = TrophyPhotoStorage.MaxFileBytes + 1024 * 1024)]
    [RequireMobileCapability(MobileApi.Models.MobileCapabilityNames.SubmitTrophy)]
    public async Task<IActionResult> Upload(
        IFormFile? photo,
        CancellationToken cancellationToken)
    {
        if (!await IsHunterAsync(cancellationToken)) return Forbid();
        if (!TryGetCurrentUserId(out var ownerUserId))
        {
            return Unauthorized(new { message = "Jou aanmeldbesonderhede kon nie bevestig word nie." });
        }

        if (photo is null)
        {
            return BadRequest(new { message = "Kies asseblief 'n foto om op te laai." });
        }

        try
        {
            var saved = await _storage.SaveAsync(photo, ownerUserId, cancellationToken);
            return Created(saved.RelativePath, new
            {
                photoPath = saved.RelativePath,
                contentType = saved.ContentType,
                length = saved.Length
            });
        }
        catch (InvalidDataException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpGet("/uploads/trophies/{ownerUserId:int}/{fileName}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetPhoto(
        int ownerUserId,
        string fileName,
        CancellationToken cancellationToken)
    {
        var photoPath = $"{TrophyPhotoStorage.RequestPath}/{ownerUserId}/{fileName}";
        if (!_storage.TryGetOwnedFile(photoPath, ownerUserId, out var photoFile) ||
            photoFile is null)
        {
            return NotFound();
        }

        var isOwner = TryGetCurrentUserId(out var currentUserId) &&
            currentUserId == ownerUserId;
        if (!isOwner &&
            !await _trophies.IsPhotoPublicAsync(photoPath, cancellationToken))
        {
            // Do not disclose whether a private photo exists.
            return NotFound();
        }

        Response.Headers["X-Content-Type-Options"] = "nosniff";
        // A public trophy can later be made private, so never let a browser or
        // intermediary retain an authorization-sensitive copy.
        Response.Headers.CacheControl = "no-store";
        return PhysicalFile(
            photoFile.FullPath,
            photoFile.ContentType,
            enableRangeProcessing: true
        );
    }

    [HttpDelete]
    [RequireMobileCapability(MobileApi.Models.MobileCapabilityNames.SubmitTrophy)]
    public async Task<IActionResult> Delete(
        [FromQuery] string photoPath,
        CancellationToken cancellationToken)
    {
        if (!await IsHunterAsync(cancellationToken)) return Forbid();
        if (!TryGetCurrentUserId(out var ownerUserId))
        {
            return Unauthorized(new { message = "Jou aanmeldbesonderhede kon nie bevestig word nie." });
        }

        if (!_storage.IsOwnedPath(photoPath, ownerUserId))
        {
            return Forbid();
        }

        var deleted = await _storage.DeleteIfOwnedAsync(
            photoPath,
            ownerUserId,
            cancellationToken
        );
        return deleted ? NoContent() : NotFound();
    }

    private bool TryGetCurrentUserId(out int userId)
    {
        var claimValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(claimValue, out userId);
    }

    private async Task<bool> IsHunterAsync(CancellationToken cancellationToken)
    {
        var caller = await _mobileAccess.ResolveAsync(User, cancellationToken);
        return caller.Role == Models.MobileRole.Hunter && caller.Can(Models.MobileCapabilityNames.SubmitTrophy);
    }
}
