using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MobileApi.Database;
using MobileApi.Models;
using MobileApi.Options;
using MobileApi.Services;

namespace MobileApi.Controllers;

[ApiController]
[Authorize(AuthenticationSchemes = InternalServiceAuthenticationHandler.BearerOrInternal)]
[Route("api/bookings/{bookingId:int}/media")]
public sealed class BookingMediaController : ControllerBase
{
    private readonly IMobileAccessService _access;
    private readonly ISharedBookingRepository _repository;
    private readonly ISharedBookingStorage _storage;
    private readonly SharedBookingOptions _options;

    public BookingMediaController(
        IMobileAccessService access,
        ISharedBookingRepository repository,
        ISharedBookingStorage storage,
        IOptions<SharedBookingOptions> options)
    {
        _access = access;
        _repository = repository;
        _storage = storage;
        _options = options.Value;
    }

    [HttpGet]
    public async Task<IActionResult> List(int bookingId, CancellationToken cancellationToken)
    {
        var auth = await AuthorizeAsync(bookingId, MobileCapabilityNames.ViewBookingMedia, cancellationToken);
        if (auth.Error is not null) return auth.Error;
        var media = await _repository.ListMediaAsync(bookingId, cancellationToken);
        if (auth.Caller!.Role == MobileRole.Vg)
        {
            foreach (var item in media)
            {
                item.Latitude = null;
                item.Longitude = null;
                item.MatchedRoutePointIndex = null;
                item.MatchedPinId = null;
            }
        }
        return Ok(media);
    }

    /// <summary>Upper bound for the multipart request carrying one clip; the configured MaxVideoClipBytes is enforced inside it.</summary>
    private const long MaxVideoRequestBytes = 260L * 1024 * 1024;

    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(32 * 1024 * 1024)]
    public async Task<IActionResult> Upload(
        int bookingId,
        IFormFile? photo,
        [FromForm] DateTime? originalCaptureTimestamp,
        [FromForm] decimal? latitude,
        [FromForm] decimal? longitude,
        [FromForm] int? orientation,
        [FromForm] string? caption,
        CancellationToken cancellationToken)
    {
        var auth = await AuthorizeAsync(bookingId, MobileCapabilityNames.UploadBookingMedia, cancellationToken);
        if (auth.Error is not null) return auth.Error;
        if (photo is null) return BadRequest(new { message = "Kies asseblief 'n foto om op te laai." });
        if (ValidateMetadata(caption, latitude, longitude, orientation, originalCaptureTimestamp, "foto") is { } invalid)
            return invalid;

        var usage = await _repository.GetMediaUsageAsync(bookingId, auth.Caller!.UserId, cancellationToken);
        if (usage.BookingCount >= _options.MaxPhotosPerBooking || usage.UploaderCount >= _options.MaxPhotosPerUploader)
            return Conflict(new { message = "Die foto-limiet vir hierdie bespreking is bereik." });
        if (usage.BookingBytes + photo.Length > _options.MaxBookingMediaBytes)
            return Conflict(new { message = "Die bespreking se totale mediaberginglimiet is bereik." });

        StoredBookingMedia stored;
        try
        {
            stored = await _storage.SavePhotoAsync(bookingId, auth.Caller.UserId, photo, cancellationToken);
        }
        catch (InvalidDataException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
        return await RecordMediaAsync(bookingId, auth.Caller, stored, photo.FileName, "Photo",
            originalCaptureTimestamp, latitude, longitude, orientation, caption, cancellationToken);
    }

    /// <summary>
    /// Short video clip (VG only) that the recap renderer splices in between the
    /// photos at its capture time. The clip's audio is never used in the recap.
    /// </summary>
    [HttpPost("videos")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxVideoRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxVideoRequestBytes)]
    public async Task<IActionResult> UploadVideo(
        int bookingId,
        IFormFile? video,
        [FromForm] DateTime? originalCaptureTimestamp,
        [FromForm] decimal? latitude,
        [FromForm] decimal? longitude,
        [FromForm] string? caption,
        CancellationToken cancellationToken)
    {
        var auth = await AuthorizeAsync(bookingId, MobileCapabilityNames.UploadBookingVideo, cancellationToken);
        if (auth.Error is not null) return auth.Error;
        if (video is null) return BadRequest(new { message = "Kies asseblief 'n videogreep om op te laai." });
        if (ValidateMetadata(caption, latitude, longitude, null, originalCaptureTimestamp, "video") is { } invalid)
            return invalid;

        var usage = await _repository.GetMediaUsageAsync(bookingId, auth.Caller!.UserId, cancellationToken);
        if (usage.BookingCount >= _options.MaxPhotosPerBooking || usage.UploaderVideoCount >= _options.MaxVideosPerUploader)
            return Conflict(new { message = "Die videolimiet vir hierdie bespreking is bereik." });
        if (usage.BookingBytes + video.Length > _options.MaxBookingMediaBytes)
            return Conflict(new { message = "Die bespreking se totale mediaberginglimiet is bereik." });

        StoredBookingMedia stored;
        try
        {
            stored = await _storage.SaveVideoAsync(bookingId, auth.Caller.UserId, video, cancellationToken);
        }
        catch (InvalidDataException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
        return await RecordMediaAsync(bookingId, auth.Caller, stored, video.FileName, "Video",
            originalCaptureTimestamp, latitude, longitude, null, caption, cancellationToken);
    }

    private IActionResult? ValidateMetadata(string? caption, decimal? latitude, decimal? longitude, int? orientation,
        DateTime? capturedAt, string noun)
    {
        if (caption?.Length > 500) return BadRequest(new { message = "Die byskrif is te lank." });
        if ((latitude is null) != (longitude is null) || latitude is < -90 or > 90 || longitude is < -180 or > 180)
            return BadRequest(new { message = $"Die {noun} se GPS-koördinate is ongeldig." });
        if (orientation is not null and (< 1 or > 8) ||
            capturedAt?.ToUniversalTime() is { Year: < 2000 } ||
            capturedAt?.ToUniversalTime() > DateTime.UtcNow.AddDays(1))
            return BadRequest(new { message = $"Die {noun} se metadata is ongeldig." });
        return null;
    }

    /// <summary>Matches freshly stored media to the selected route and records it; the stored file is removed again if the row cannot be written.</summary>
    private async Task<IActionResult> RecordMediaAsync(int bookingId, MobileRoleContext caller, StoredBookingMedia stored,
        string? fileName, string sourceType, DateTime? capturedAt, decimal? latitude, decimal? longitude, int? orientation,
        string? caption, CancellationToken cancellationToken)
    {
        long? mediaId = null;
        try
        {
            var selected = (await _repository.ListRoutesAsync(bookingId, includePrecisePoints: true, cancellationToken))
                .FirstOrDefault(route => route.SelectedForRecap);
            var match = BookingPhotoMatcher.Match(selected, capturedAt, latitude, longitude);
            var safeName = Path.GetFileName(fileName ?? string.Empty);
            var media = new BookingMediaDto
            {
                BookingId = bookingId,
                UploaderUserId = caller.UserId,
                UploaderUserRoleId = caller.UserRoleId,
                UploaderRole = MobileAccessService.RoleName(caller.Role),
                OriginalFileName = safeName.Length is > 0 and <= 255 ? safeName : sourceType.ToLowerInvariant(),
                PrivateStorageReference = stored.StorageReference,
                ContentType = stored.ContentType,
                FileSizeBytes = stored.Length,
                Width = stored.Width,
                Height = stored.Height,
                SourceType = sourceType,
                DurationSeconds = stored.DurationSeconds,
                OriginalCaptureTimestamp = capturedAt?.ToUniversalTime(),
                Latitude = latitude,
                Longitude = longitude,
                Orientation = orientation,
                Caption = caption?.Trim(),
                MatchMethod = match.Method,
                MatchConfidence = match.Confidence,
                MatchedRoutePointIndex = match.RoutePointIndex,
                MatchedPinId = match.PinId
            };
            mediaId = await _repository.AddMediaAsync(media, cancellationToken);
            var created = await _repository.GetMediaAsync(mediaId.Value, cancellationToken);
            return CreatedAtAction("Content", "BookingMediaContent", new { mediaId }, created);
        }
        catch
        {
            if (mediaId is null)
                await _storage.DeleteAsync(stored.StorageReference, cancellationToken);
            throw;
        }
    }

    [HttpPatch("{mediaId:long}")]
    public async Task<IActionResult> Update(int bookingId, long mediaId, [FromBody] BookingMediaUpdateRequest request, CancellationToken cancellationToken)
    {
        var auth = await AuthorizeAsync(bookingId, MobileCapabilityNames.EditOwnBookingMedia, cancellationToken);
        if (auth.Error is not null) return auth.Error;
        var media = await _repository.GetMediaAsync(mediaId, cancellationToken);
        if (media is null || media.BookingId != bookingId) return NotFound();
        if (media.UploaderUserId != auth.Caller!.UserId) return Forbid();
        if (request.Caption?.Length > 500 || request.MatchedRoutePointIndex is < 0 ||
            request.MatchedPinId?.Length > 120 ||
            request.AdjustedCaptureTimestamp?.ToUniversalTime() is { Year: < 2000 } ||
            request.AdjustedCaptureTimestamp?.ToUniversalTime() > DateTime.UtcNow.AddDays(1))
            return BadRequest(new { message = "Die mediaverandering is ongeldig." });

        if (auth.Caller.Role != MobileRole.Vg && (request.MatchedRoutePointIndex is not null || !string.IsNullOrWhiteSpace(request.MatchedPinId)))
        {
            var selected = (await _repository.ListRoutesAsync(bookingId, includePrecisePoints: true, cancellationToken))
                .FirstOrDefault(route => route.SelectedForRecap);
            if (selected is null ||
                request.MatchedRoutePointIndex is { } index && index >= selected.Points.Count ||
                !string.IsNullOrWhiteSpace(request.MatchedPinId) &&
                    selected.Pins.All(pin => pin.Id != request.MatchedPinId.Trim()))
            {
                return BadRequest(new { message = "Kies 'n geldige punt of jagpen op die PH-geselekteerde roete." });
            }
        }
        if (auth.Caller.Role == MobileRole.Vg)
        {
            // A VG never sees route positions (List strips them), so an edit from a VG
            // only changes caption/time and must keep the existing route match intact.
            request.MatchedRoutePointIndex = media.MatchedRoutePointIndex;
            request.MatchedPinId = media.MatchedPinId;
            request.MatchMethod = media.MatchMethod;
        }
        else
        {
            request.MatchMethod = "Manual";
        }
        return await _repository.UpdateMediaAsync(mediaId, request, cancellationToken) ? NoContent() : NotFound();
    }

    [HttpPost("{mediaId:long}/moderation")]
    public async Task<IActionResult> Moderate(int bookingId, long mediaId, [FromBody] BookingMediaModerationRequest request, CancellationToken cancellationToken)
    {
        var auth = await AuthorizeAsync(bookingId, MobileCapabilityNames.ModerateBookingMedia, cancellationToken);
        if (auth.Error is not null) return auth.Error;
        var status = request.InclusionStatus.Trim();
        if (status is not ("Included" or "Excluded")) return BadRequest(new { message = "Ongeldige insluitingstatus." });
        if (status == "Excluded" && string.IsNullOrWhiteSpace(request.Reason))
            return BadRequest(new { message = "Gee asseblief 'n rede waarom die foto uitgesluit word." });
        var media = await _repository.GetMediaAsync(mediaId, cancellationToken);
        if (media is null || media.BookingId != bookingId) return NotFound();
        return await _repository.ModerateMediaAsync(mediaId, status, request.Reason?.Trim() ?? string.Empty, cancellationToken) ? NoContent() : NotFound();
    }

    [HttpDelete("{mediaId:long}")]
    public async Task<IActionResult> Delete(int bookingId, long mediaId, CancellationToken cancellationToken)
    {
        var auth = await AuthorizeAsync(bookingId, MobileCapabilityNames.EditOwnBookingMedia, cancellationToken);
        if (auth.Error is not null) return auth.Error;
        var media = await _repository.GetMediaAsync(mediaId, cancellationToken);
        if (media is null || media.BookingId != bookingId) return NotFound();
        if (media.UploaderUserId != auth.Caller!.UserId) return Forbid();
        if (!await _repository.SoftDeleteMediaAsync(mediaId, "Deleted by an authorized participant", cancellationToken)) return NotFound();
        await _storage.DeleteAsync(media.PrivateStorageReference, cancellationToken);
        return NoContent();
    }

    private async Task<(MobileRoleContext? Caller, IActionResult? Error)> AuthorizeAsync(int bookingId, string capability, CancellationToken cancellationToken)
    {
        var caller = await _access.ResolveAsync(User, cancellationToken);
        if (!caller.Can(capability)) return (caller, Forbid());
        var booking = await _access.GetBookingAccessAsync(caller, bookingId, cancellationToken);
        if (booking is null || !booking.IsParticipant) return (caller, Forbid());
        if (!booking.IsEligible) return (caller, Conflict(new { message = "Hierdie aksie is net vir 'n voltooide, nie-gekanselleerde bespreking beskikbaar." }));
        return (caller, null);
    }
}

[ApiController]
[Authorize(AuthenticationSchemes = InternalServiceAuthenticationHandler.BearerOrInternal)]
public sealed class BookingMediaContentController : ControllerBase
{
    private readonly IMobileAccessService _access;
    private readonly ISharedBookingRepository _repository;
    private readonly ISharedBookingStorage _storage;

    public BookingMediaContentController(IMobileAccessService access, ISharedBookingRepository repository, ISharedBookingStorage storage)
    {
        _access = access; _repository = repository; _storage = storage;
    }

    [HttpGet("api/booking-media/{mediaId:long}/content", Name = "BookingMediaContent")]
    public async Task<IActionResult> Content(long mediaId, CancellationToken cancellationToken)
    {
        var media = await _repository.GetMediaAsync(mediaId, cancellationToken);
        if (media is null) return NotFound();
        var caller = await _access.ResolveAsync(User, cancellationToken);
        if (!caller.Can(MobileCapabilityNames.ViewBookingMedia)) return Forbid();
        var booking = await _access.GetBookingAccessAsync(caller, media.BookingId, cancellationToken);
        if (booking is null || !booking.IsParticipant) return NotFound();
        if (!_storage.TryGetPhoto(media.PrivateStorageReference, media.BookingId, out var file) || file is null) return NotFound();
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return PhysicalFile(file.FullPath, file.ContentType, enableRangeProcessing: true);
    }
}

internal static class BookingPhotoMatcher
{
    internal sealed record MatchResult(string Method, string Confidence, int? RoutePointIndex, string? PinId);

    public static MatchResult Match(BookingRouteDto? route, DateTime? capturedAt, decimal? latitude, decimal? longitude)
    {
        if (route is null) return new("Unmatched", "None", null, null);
        if (latitude is not null && longitude is not null && route.Points.Count > 0)
        {
            var nearest = route.Points.Select((point, index) => (index, distance: Distance((double)latitude, (double)longitude, point.Latitude, point.Longitude)))
                .OrderBy(x => x.distance).First();
            if (nearest.distance <= 500)
            {
                var confidence = nearest.distance <= 50 ? "High" : nearest.distance <= 150 ? "Medium" : "Low";
                var pin = route.Pins.OrderBy(x => Distance((double)latitude, (double)longitude, x.Latitude, x.Longitude)).FirstOrDefault();
                var pinId = pin is not null && Distance((double)latitude, (double)longitude, pin.Latitude, pin.Longitude) <= 100 ? pin.Id : null;
                return new("GPS", confidence, nearest.index, pinId);
            }
        }
        if (capturedAt is not null && route.Points.Count > 0)
        {
            var timestamp = capturedAt.Value.ToUniversalTime();
            var nearest = route.Points.Select((point, index) => (index, difference: (point.Timestamp.ToUniversalTime() - timestamp).Duration()))
                .OrderBy(x => x.difference).First();
            if (nearest.difference <= TimeSpan.FromHours(4))
            {
                var confidence = nearest.difference <= TimeSpan.FromMinutes(2) ? "High" : nearest.difference <= TimeSpan.FromMinutes(20) ? "Medium" : "Low";
                var pin = route.Pins.OrderBy(x => (x.Timestamp.ToUniversalTime() - timestamp).Duration()).FirstOrDefault();
                var pinId = pin is not null && (pin.Timestamp.ToUniversalTime() - timestamp).Duration() <= TimeSpan.FromMinutes(20) ? pin.Id : null;
                return new(pinId is null ? "Timestamp" : "Pin", confidence, nearest.index, pinId);
            }
        }
        return new("Unmatched", "None", null, null);
    }

    private static double Distance(double lat1, double lon1, double lat2, double lon2)
    {
        const double radius = 6_371_000;
        static double Rad(double value) => value * Math.PI / 180;
        var dLat = Rad(lat2 - lat1); var dLon = Rad(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return radius * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }
}
