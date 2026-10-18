using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MobileApi.Models;
using MobileApi.Options;
using MobileApi.Services;

namespace MobileApi.Controllers;

/// <summary>
/// Post-trip ratings between the Hunter, PH and VG of a booking, from the app.
/// The ratings table, its participant/eligibility rules, edit history and comment
/// moderation all belong to WebAPI (RatingController); this controller only relays
/// the signed-in app user's request there over the internal service channel, so the
/// app and the website can never disagree about who may rate whom.
/// </summary>
[ApiController]
[Authorize]
[Route("api/bookings/{bookingId:int}/team-ratings")]
public sealed class TeamRatingsController : ControllerBase
{
    private readonly IMobileAccessService _access;
    private readonly IHttpClientFactory _httpFactory;
    private readonly InternalApiOptions _internal;

    public TeamRatingsController(IMobileAccessService access, IHttpClientFactory httpFactory, IOptions<InternalApiOptions> internalOptions)
    {
        _access = access;
        _httpFactory = httpFactory;
        _internal = internalOptions.Value;
    }

    /// <summary>The other people on the booking the caller can rate, with the caller's current ratings.</summary>
    [HttpGet]
    public async Task<IActionResult> Get(int bookingId, CancellationToken cancellationToken)
    {
        var (userId, error) = await ResolveCallerAsync(cancellationToken);
        if (error is not null) return error;
        return await RelayAsync(HttpMethod.Get, $"api/rating/booking/{bookingId}/team", userId, null, cancellationToken);
    }

    /// <summary>Saves (or corrects) the caller's rating of one participant.</summary>
    [HttpPost]
    public async Task<IActionResult> Rate(int bookingId, [FromBody] TeamRatingRequest request, CancellationToken cancellationToken)
    {
        var (userId, error) = await ResolveCallerAsync(cancellationToken);
        if (error is not null) return error;
        var payload = JsonSerializer.Serialize(new
        {
            bookingId,
            raterUserId = userId,
            rateeUserId = request.RateeUserId,
            rateeRoleCode = request.RateeRoleCode,
            stars = request.Stars,
            comment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim()
        });
        return await RelayAsync(HttpMethod.Post, "api/rating/setRoleRating", userId,
            new StringContent(payload, Encoding.UTF8, "application/json"), cancellationToken);
    }

    private async Task<(int UserId, IActionResult? Error)> ResolveCallerAsync(CancellationToken cancellationToken)
    {
        // Re-validates the selected role against the database (suspended/unverified users are Restricted).
        var caller = await _access.ResolveAsync(User, cancellationToken);
        if (!caller.IsSupported || caller.UserId <= 0) return (0, Forbid());
        return (caller.UserId, null);
    }

    private async Task<IActionResult> RelayAsync(HttpMethod method, string path, int userId, HttpContent? content, CancellationToken cancellationToken)
    {
        if (_internal.Key.Length < 32 || string.IsNullOrWhiteSpace(_internal.WebApiBaseUrl))
            return StatusCode(503, new { message = "Graderings is nog nie op hierdie bediener opgestel nie." });
        using var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.Add(InternalServiceAuthenticationHandler.KeyHeader, _internal.Key);
        request.Headers.Add(InternalServiceAuthenticationHandler.UserIdHeader, userId.ToString(CultureInfo.InvariantCulture));
        try
        {
            using var response = await _httpFactory.CreateClient("webapi-internal").SendAsync(request, cancellationToken);
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
            return StatusCode(502, new { message = "Die graderingsdiens is tans nie bereikbaar nie." });
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return StatusCode(504, new { message = "Die graderingsdiens het te lank geneem om te antwoord." });
        }
    }
}
