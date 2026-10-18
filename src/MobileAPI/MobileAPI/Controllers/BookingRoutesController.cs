using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MobileApi.Database;
using MobileApi.Models;
using MobileApi.Options;
using MobileApi.Services;

namespace MobileApi.Controllers;

[ApiController]
[Authorize]
[Route("api/bookings/{bookingId:int}/routes")]
public sealed class BookingRoutesController : ControllerBase
{
    private readonly IMobileAccessService _access;
    private readonly ISharedBookingRepository _repository;
    private readonly SharedBookingOptions _options;

    public BookingRoutesController(IMobileAccessService access, ISharedBookingRepository repository, IOptions<SharedBookingOptions> options)
    { _access = access; _repository = repository; _options = options.Value; }

    [HttpPost]
    public async Task<IActionResult> Submit(int bookingId, [FromBody] SubmitBookingRouteRequest request, CancellationToken cancellationToken)
    {
        var caller = await _access.ResolveAsync(User, cancellationToken);
        var error = await BookingError(caller, bookingId, MobileCapabilityNames.SubmitRoute, cancellationToken);
        if (error is not null) return error;
        if (caller.Role != MobileRole.Hunter) return Forbid();
        var booking = await _access.GetBookingAccessAsync(caller, bookingId, cancellationToken);
        var validation = Validate(request, booking!);
        if (validation is not null) return BadRequest(new { message = validation });
        var bytes = JsonSerializer.SerializeToUtf8Bytes(request, JsonDefaults.Options).Length;
        if (bytes > _options.MaxRoutePayloadBytes) return StatusCode(StatusCodes.Status413PayloadTooLarge, new { message = "Die roetedata is te groot." });
        try
        {
            var routeId = await _repository.AddRouteAsync(bookingId, caller, request, cancellationToken);
            var route = await _repository.GetRouteAsync(routeId, includePrecisePoints: true, cancellationToken);
            return CreatedAtAction(nameof(GetSelected), new { bookingId }, route);
        }
        catch (Microsoft.Data.SqlClient.SqlException exception) when (exception.Number is 2601 or 2627)
        {
            return Conflict(new { message = "Hierdie roeteweergawe is reeds ingedien." });
        }
    }

    [HttpGet]
    public async Task<IActionResult> List(int bookingId, CancellationToken cancellationToken)
    {
        var caller = await _access.ResolveAsync(User, cancellationToken);
        var capability = caller.Role == MobileRole.Ph ? MobileCapabilityNames.SelectRoute : MobileCapabilityNames.ViewRecap;
        var error = await BookingError(caller, bookingId, capability, cancellationToken);
        if (error is not null) return error;
        var includePrecise = caller.Role is MobileRole.Hunter or MobileRole.Ph;
        return Ok(await _repository.ListRoutesAsync(bookingId, includePrecise, cancellationToken));
    }

    [HttpPut("{routeId:long}/selection")]
    public async Task<IActionResult> Select(int bookingId, long routeId, CancellationToken cancellationToken)
    {
        var caller = await _access.ResolveAsync(User, cancellationToken);
        var error = await BookingError(caller, bookingId, MobileCapabilityNames.SelectRoute, cancellationToken);
        if (error is not null) return error;
        return await _repository.SelectRouteAsync(bookingId, routeId, caller.UserId, cancellationToken)
            ? NoContent()
            : NotFound(new { message = "Die roete behoort nie aan hierdie bespreking nie." });
    }

    [HttpGet("selected")]
    public async Task<IActionResult> GetSelected(int bookingId, CancellationToken cancellationToken)
    {
        var caller = await _access.ResolveAsync(User, cancellationToken);
        var error = await BookingError(caller, bookingId, MobileCapabilityNames.ViewRecap, cancellationToken);
        if (error is not null) return error;
        var includePrecise = caller.Role is MobileRole.Hunter or MobileRole.Ph;
        var selected = (await _repository.ListRoutesAsync(bookingId, includePrecise, cancellationToken)).FirstOrDefault(x => x.SelectedForRecap);
        return selected is null ? NotFound(new { message = "Geen roete is nog vir die recap gekies nie." }) : Ok(selected);
    }

    private async Task<IActionResult?> BookingError(MobileRoleContext caller, int bookingId, string capability, CancellationToken cancellationToken)
    {
        if (!caller.Can(capability)) return Forbid();
        var booking = await _access.GetBookingAccessAsync(caller, bookingId, cancellationToken);
        if (booking is null || !booking.IsParticipant) return Forbid();
        return booking.IsEligible ? null : Conflict(new { message = "Roetes kan net vir 'n voltooide bespreking gesinchroniseer word." });
    }

    private string? Validate(SubmitBookingRouteRequest request, BookingAccess booking)
    {
        if (string.IsNullOrWhiteSpace(request.SourceHuntSessionId) || request.SourceHuntSessionId.Length > 120) return "Die jagroete se sessie-ID is ongeldig.";
        if (request.Points.Count < 2 || request.Points.Count > _options.MaxRoutePoints) return $"Die roete moet tussen 2 en {_options.MaxRoutePoints} punte bevat.";
        if (!double.IsFinite(request.DistanceMeters) || request.DistanceMeters < 0 || request.DistanceMeters > 10_000_000 ||
            request.DurationSeconds < 0 || request.DurationSeconds > 7 * 24 * 60 * 60 || request.RouteVersion <= 0)
            return "Die roetesamevatting is ongeldig.";
        DateTime? previous = null;
        foreach (var point in request.Points)
        {
            if (!double.IsFinite(point.Latitude) || !double.IsFinite(point.Longitude) || point.Latitude is < -90 or > 90 || point.Longitude is < -180 or > 180) return "Die roete bevat ongeldige GPS-data.";
            var timestamp = point.Timestamp.ToUniversalTime();
            if (timestamp.Year < 2000 || timestamp > DateTime.UtcNow.AddDays(1) ||
                timestamp.Date < booking.StartDate.Date.AddDays(-1) || timestamp.Date > booking.EndDate.Date.AddDays(1) ||
                (previous is not null && timestamp < previous))
                return "Die roete se tydstempels is ongeldig, buite die bespreking, of nie in volgorde nie.";
            previous = timestamp;
        }
        var calculatedDistance = request.Points.Zip(request.Points.Skip(1))
            .Sum(pair => Distance(pair.First.Latitude, pair.First.Longitude, pair.Second.Latitude, pair.Second.Longitude));
        if (Math.Abs(calculatedDistance - request.DistanceMeters) > Math.Max(250, calculatedDistance * .35))
            return "Die roete se afstand stem nie met sy GPS-punte ooreen nie.";
        if (request.Pins.Count > 5_000 || request.Pins.Any(pin =>
                !double.IsFinite(pin.Latitude) || !double.IsFinite(pin.Longitude) ||
                pin.Latitude is < -90 or > 90 || pin.Longitude is < -180 or > 180 ||
                string.IsNullOrWhiteSpace(pin.Id) || pin.Id.Length > 120 || pin.Type.Length > 80 ||
                pin.Note?.Length > 500 || pin.Timestamp.ToUniversalTime().Year < 2000 ||
                pin.Timestamp.ToUniversalTime().Date < booking.StartDate.Date.AddDays(-1) ||
                pin.Timestamp.ToUniversalTime().Date > booking.EndDate.Date.AddDays(1)))
            return "Die roete bevat ongeldige jagpenne.";
        return null;
    }

    private static double Distance(double lat1, double lon1, double lat2, double lon2)
    {
        const double radius = 6_371_000;
        static double Rad(double value) => value * Math.PI / 180;
        var dLat = Rad(lat2 - lat1);
        var dLon = Rad(lon2 - lon1);
        var value = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
            Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) *
            Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return radius * 2 * Math.Atan2(Math.Sqrt(value), Math.Sqrt(1 - value));
    }
}
