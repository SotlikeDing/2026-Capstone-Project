using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using WebAPI.Database;
using WebAPI.Services;

namespace WebAPI.Controllers;

/// <summary>
/// Website entry point for a VG's photos and short video clips on a booking they
/// filmed. The media itself, its limits and the recap pipeline live in MobileAPI;
/// this controller authenticates the website user from their own JWT, resolves
/// their VG userRoleId and relays the call to MobileAPI over the internal service
/// channel. MobileAPI re-checks the role and that the VG is on a finished booking,
/// so nothing here widens who may touch which booking's media.
/// </summary>
[ApiController]
[Authorize]
[Route("api/vg/bookings/{bookingId:int}")]
public sealed class VgBookingMediaController : ControllerBase
{
    public const string HttpClientName = "mobileapi-internal";
    private const long MaxUploadRequestBytes = 270L * 1024 * 1024;

    private readonly IHttpClientFactory _httpFactory;
    private readonly InternalApiOptions _internal;
    private readonly ResourceOwnership_db _ownership;

    public VgBookingMediaController(IHttpClientFactory httpFactory, IOptions<InternalApiOptions> internalOptions, ResourceOwnership_db ownership)
    {
        _httpFactory = httpFactory;
        _internal = internalOptions.Value;
        _ownership = ownership;
    }

    [HttpGet("media")]
    public Task<IActionResult> ListMedia(int bookingId, CancellationToken cancellationToken) =>
        RelayAsync(bookingId, HttpMethod.Get, $"api/bookings/{bookingId}/media", null, cancellationToken);

    [HttpGet("recaps")]
    public Task<IActionResult> ListRecaps(int bookingId, CancellationToken cancellationToken) =>
        RelayAsync(bookingId, HttpMethod.Get, $"api/bookings/{bookingId}/recaps", null, cancellationToken);

    [HttpPost("media/photos")]
    [RequestSizeLimit(40L * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 40L * 1024 * 1024)]
    public Task<IActionResult> UploadPhoto(int bookingId, IFormFile? file, [FromForm] string? caption,
        [FromForm] DateTime? capturedAt, CancellationToken cancellationToken) =>
        UploadAsync(bookingId, "photo", $"api/bookings/{bookingId}/media", file, caption, capturedAt, cancellationToken);

    [HttpPost("media/videos")]
    [RequestSizeLimit(MaxUploadRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadRequestBytes)]
    public Task<IActionResult> UploadVideo(int bookingId, IFormFile? file, [FromForm] string? caption,
        [FromForm] DateTime? capturedAt, CancellationToken cancellationToken) =>
        UploadAsync(bookingId, "video", $"api/bookings/{bookingId}/media/videos", file, caption, capturedAt, cancellationToken);

    public sealed class MediaDetailsRequest
    {
        public string? Caption { get; set; }
        /// <summary>When the photo/clip was taken - orders it within the story. Null keeps the original capture time.</summary>
        public DateTime? CapturedAt { get; set; }
    }

    /// <summary>The uploader edits caption and capture time together (MobileAPI replaces both; route matches are kept for a VG).</summary>
    [HttpPut("media/{mediaId:long}")]
    public Task<IActionResult> UpdateDetails(int bookingId, long mediaId, [FromBody] MediaDetailsRequest request, CancellationToken cancellationToken)
    {
        var body = new StringContent(JsonSerializer.Serialize(new
        {
            caption = string.IsNullOrWhiteSpace(request.Caption) ? null : request.Caption.Trim(),
            adjustedCaptureTimestamp = request.CapturedAt?.ToUniversalTime()
        }), Encoding.UTF8, "application/json");
        return RelayAsync(bookingId, HttpMethod.Patch, $"api/bookings/{bookingId}/media/{mediaId}", body, cancellationToken);
    }

    [HttpDelete("media/{mediaId:long}")]
    public Task<IActionResult> DeleteMedia(int bookingId, long mediaId, CancellationToken cancellationToken) =>
        RelayAsync(bookingId, HttpMethod.Delete, $"api/bookings/{bookingId}/media/{mediaId}", null, cancellationToken);

    /// <summary>Streams one private photo/clip back for preview. MobileAPI only serves media of bookings the VG is on.</summary>
    [HttpGet("media/{mediaId:long}/content")]
    public async Task<IActionResult> MediaContent(int bookingId, long mediaId, CancellationToken cancellationToken)
    {
        var (request, error) = await BuildRequestAsync(bookingId, HttpMethod.Get, $"api/booking-media/{mediaId}/content", null);
        if (error is not null) return error;
        var client = _httpFactory.CreateClient(HttpClientName);
        var response = await client.SendAsync(request!, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var status = (int)response.StatusCode;
            response.Dispose();
            return StatusCode(status);
        }
        HttpContext.Response.RegisterForDispose(response);
        Response.Headers.CacheControl = "private, no-store";
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return File(stream, response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream");
    }

    private async Task<IActionResult> UploadAsync(int bookingId, string fieldName, string path, IFormFile? file,
        string? caption, DateTime? capturedAt, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { message = "Kies asseblief 'n lêer om op te laai." });

        await using var stream = file.OpenReadStream();
        var fileContent = new StreamContent(stream);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.TryParse(file.ContentType, out var type)
            ? type
            : new MediaTypeHeaderValue("application/octet-stream");
        var form = new MultipartFormDataContent { { fileContent, fieldName, Path.GetFileName(file.FileName) } };
        if (!string.IsNullOrWhiteSpace(caption)) form.Add(new StringContent(caption.Trim()), "caption");
        if (capturedAt is { } at)
            form.Add(new StringContent(at.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture)), "originalCaptureTimestamp");
        return await RelayAsync(bookingId, HttpMethod.Post, path, form, cancellationToken);
    }

    private async Task<IActionResult> RelayAsync(int bookingId, HttpMethod method, string path, HttpContent? content, CancellationToken cancellationToken)
    {
        var (request, error) = await BuildRequestAsync(bookingId, method, path, content);
        if (error is not null) return error;
        try
        {
            var client = _httpFactory.CreateClient(HttpClientName);
            using var response = await client.SendAsync(request!, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrEmpty(body)) return StatusCode((int)response.StatusCode);
            return new ContentResult
            {
                StatusCode = (int)response.StatusCode,
                Content = body,
                ContentType = response.Content.Headers.ContentType?.ToString() ?? "application/json"
            };
        }
        catch (HttpRequestException)
        {
            return StatusCode(502, new { message = "Die mediadiens is tans nie bereikbaar nie. Probeer asseblief later weer." });
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return StatusCode(504, new { message = "Die mediadiens het te lank geneem om te antwoord." });
        }
    }

    private async Task<(HttpRequestMessage? Request, IActionResult? Error)> BuildRequestAsync(int bookingId, HttpMethod method, string path, HttpContent? content)
    {
        if (!_internal.IsConfigured || string.IsNullOrWhiteSpace(_internal.MobileApiBaseUrl))
            return (null, StatusCode(503, new { message = "Media-oplaai is nog nie op hierdie bediener opgestel nie." }));
        var userId = User.GetUserId();
        if (userId is null) return (null, Unauthorized());
        var vgUserRoleId = await _ownership.GetVgUserRoleIdForUser(userId.Value, bookingId);
        if (vgUserRoleId is null)
            return (null, StatusCode(403, new { message = "Slegs 'n videograaf (VG) kan hierdie bespreking se media bestuur." }));

        var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.Add(InternalServiceAuthenticationHandler.KeyHeader, _internal.Key);
        request.Headers.Add(InternalServiceAuthenticationHandler.UserIdHeader, userId.Value.ToString(CultureInfo.InvariantCulture));
        // No role header: MobileAPI reads the role code for this userRoleId from the database itself.
        request.Headers.Add(InternalServiceAuthenticationHandler.UserRoleIdHeader, vgUserRoleId.Value.ToString(CultureInfo.InvariantCulture));
        return (request, null);
    }
}
