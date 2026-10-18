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
[Route("api/bookings/{bookingId:int}/recaps")]
public sealed class BookingRecapsController : ControllerBase
{
    private readonly IMobileAccessService _access;
    private readonly ISharedBookingRepository _repository;
    private readonly SharedBookingOptions _options;

    public BookingRecapsController(IMobileAccessService access, ISharedBookingRepository repository, IOptions<SharedBookingOptions> options)
    { _access = access; _repository = repository; _options = options.Value; }

    [HttpPost]
    public async Task<IActionResult> RequestRecap(int bookingId, [FromBody] RequestRecapRequest request, CancellationToken cancellationToken)
    {
        var (caller, booking, error) = await AuthorizeAsync(bookingId, MobileCapabilityNames.GenerateRecap, cancellationToken);
        if (error is not null) return error;
        var title = string.IsNullOrWhiteSpace(request.Title) ? $"{booking!.Title} recap" : request.Title.Trim();
        if (title.Length > 180) return BadRequest(new { message = "Die recaptitel is te lank." });
        try
        {
            var recap = await _repository.QueueRecapAsync(bookingId, caller!.UserId, title, _options.MaxRecapJobsPerBooking, cancellationToken);
            recap.PlaybackPath = $"/api/recaps/{recap.RecapId}/content";
            return AcceptedAtAction(nameof(Get), new { bookingId, recapId = recap.RecapId }, recap);
        }
        catch (Microsoft.Data.SqlClient.SqlException exception) when (exception.Number is >= 51001 and <= 51004)
        {
            return Conflict(new { message = exception.Message });
        }
    }

    [HttpGet]
    [Authorize(AuthenticationSchemes = InternalServiceAuthenticationHandler.BearerOrInternal)]
    public async Task<IActionResult> List(int bookingId, CancellationToken cancellationToken)
    {
        var (_, _, error) = await AuthorizeAsync(bookingId, MobileCapabilityNames.ViewRecap, cancellationToken);
        if (error is not null) return error;
        var recaps = await _repository.ListRecapsAsync(bookingId, cancellationToken);
        foreach (var recap in recaps) if (recap.Status is "Completed" or "Published") recap.PlaybackPath = $"/api/recaps/{recap.RecapId}/content";
        return Ok(recaps);
    }

    [HttpGet("{recapId:long}")]
    public async Task<IActionResult> Get(int bookingId, long recapId, CancellationToken cancellationToken)
    {
        var (_, _, error) = await AuthorizeAsync(bookingId, MobileCapabilityNames.ViewRecap, cancellationToken);
        if (error is not null) return error;
        var recap = await _repository.GetRecapAsync(recapId, cancellationToken);
        if (recap is null || recap.BookingId != bookingId) return NotFound();
        if (recap.Status is "Completed" or "Published") recap.PlaybackPath = $"/api/recaps/{recap.RecapId}/content";
        return Ok(recap);
    }

    [HttpPost("{recapId:long}/retry")]
    public async Task<IActionResult> Retry(int bookingId, long recapId, CancellationToken cancellationToken)
    {
        var (_, _, error) = await AuthorizeAsync(bookingId, MobileCapabilityNames.GenerateRecap, cancellationToken);
        if (error is not null) return error;
        var recap = await _repository.GetRecapAsync(recapId, cancellationToken);
        if (recap is null || recap.BookingId != bookingId) return NotFound();
        return await _repository.RetryRecapAsync(recapId, cancellationToken)
            ? Accepted(new { message = "Die recap is weer in die ry geplaas." })
            : Conflict(new { message = "Net 'n mislukte recap kan weer probeer word." });
    }

    private async Task<(MobileRoleContext?, BookingAccess?, IActionResult?)> AuthorizeAsync(int bookingId, string capability, CancellationToken cancellationToken)
    {
        var caller = await _access.ResolveAsync(User, cancellationToken);
        if (!caller.Can(capability)) return (caller, null, Forbid());
        var booking = await _access.GetBookingAccessAsync(caller, bookingId, cancellationToken);
        if (booking is null || !booking.IsParticipant) return (caller, booking, Forbid());
        if (!booking.IsEligible) return (caller, booking, Conflict(new { message = "Recaps is net vir 'n voltooide bespreking beskikbaar." }));
        return (caller, booking, null);
    }
}

[ApiController]
[Authorize]
public sealed class RecapContentController : ControllerBase
{
    private readonly IMobileAccessService _access;
    private readonly ISharedBookingRepository _repository;
    private readonly ISharedBookingStorage _storage;
    public RecapContentController(IMobileAccessService access, ISharedBookingRepository repository, ISharedBookingStorage storage)
    { _access = access; _repository = repository; _storage = storage; }

    [HttpGet("api/recaps/{recapId:long}/content")]
    public async Task<IActionResult> Content(long recapId, CancellationToken cancellationToken)
    {
        var recap = await _repository.GetRecapAsync(recapId, cancellationToken);
        if (recap is null || recap.Status is not ("Completed" or "Published")) return NotFound();
        var caller = await _access.ResolveAsync(User, cancellationToken);
        if (!caller.Can(MobileCapabilityNames.ViewRecap)) return Forbid();
        var booking = await _access.GetBookingAccessAsync(caller, recap.BookingId, cancellationToken);
        if (booking is null || !booking.IsParticipant) return NotFound();
        if (!_storage.TryGetRecap(recap.OutputPrivateStorageReference, recap.BookingId, out var file) || file is null) return NotFound();
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return PhysicalFile(file.FullPath, "video/mp4", enableRangeProcessing: true);
    }
}
