using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MobileApi.Database;
using MobileApi.Models;
using MobileApi.Models.RowlandWard;
using MobileApi.Services;
using MobileApi.Validation;

namespace MobileApi.Controllers;

[ApiController]
[Authorize]
[Route("api/trophy-submissions")]
public sealed class TrophySubmissionsController : ControllerBase
{
    private readonly IMobileAccessService _access;
    private readonly ITrophySubmissionsRepository _repository;
    private readonly ISharedBookingRepository _media;
    private readonly ITrophyPhotoStorage _photos;
    private readonly IRowlandWardEvaluationService _evaluation;
    private readonly IUserProfilePreferencesRepository _preferences;

    public TrophySubmissionsController(IMobileAccessService access, ITrophySubmissionsRepository repository,
        ISharedBookingRepository media, ITrophyPhotoStorage photos, IRowlandWardEvaluationService evaluation,
        IUserProfilePreferencesRepository preferences)
    { _access=access; _repository=repository; _media=media; _photos=photos; _evaluation=evaluation; _preferences=preferences; }

    [HttpPost]
    public async Task<IActionResult> Submit([FromBody] SubmitTrophyRequest request, CancellationToken cancellationToken)
    {
        var caller = await _access.ResolveAsync(User, cancellationToken);
        if (!caller.Can(MobileCapabilityNames.SubmitTrophy) || caller.Role != MobileRole.Hunter) return Forbid();
        var booking = await _access.GetBookingAccessAsync(caller, request.BookingId, cancellationToken);
        if (booking is null || !booking.IsParticipant) return Forbid();
        if (!booking.IsEligible) return Conflict(new { message = "'n Trofee kan net vanuit 'n voltooide bespreking ingedien word." });
        if (request.HarvestDate.Date < booking.StartDate.Date || request.HarvestDate.Date > booking.EndDate.Date)
            return BadRequest(new { message = "Die oesdatum moet binne die bespreking se datumreeks val." });
        if (!_photos.IsOwnedPath(request.TrophyPhotoPath, caller.UserId))
            return BadRequest(new { message = "Laai die trofeefoto eers veilig deur die app op." });
        var save = ToSaveRequest(request);
        if (TrophySaveRequestValidator.GetError(save) is { } validation) return BadRequest(new { message = validation });
        if (string.IsNullOrWhiteSpace(request.LocationText) || string.IsNullOrWhiteSpace(request.FirearmType))
            return BadRequest(new { message = "Ligging en vuurwapentipe is verpligtend." });
        if (request.Notes?.Length > 4000)
            return BadRequest(new { message = "Die notas is te lank." });
        var supportingMediaIds = request.SupportingMediaIds.Distinct().ToList();
        if (supportingMediaIds.Count > 50)
            return BadRequest(new { message = "Kies hoogstens 50 ondersteunende jagfoto's." });
        foreach (var mediaId in supportingMediaIds)
        {
            var item = await _media.GetMediaAsync(mediaId, cancellationToken);
            if (item is null || item.BookingId != request.BookingId) return BadRequest(new { message = "'n Ondersteunende foto behoort nie aan hierdie bespreking nie." });
        }
        if (request.RowlandWard is not null)
        {
            if (await _preferences.GetRowlandWardEnabledAsync(caller.UserId) != true)
                return BadRequest(new { message = "Skakel eers Rowland Ward in jou profiel aan." });
            try { _ = _evaluation.Evaluate(request.RowlandWard); }
            catch (RowlandWardEvaluationException exception) { return BadRequest(new { message = exception.Message }); }
        }
        request.SupportingMediaIds = supportingMediaIds;
        var id = await _repository.CreateAsync(caller, request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { submissionId=id }, new { submissionId=id, status="Pending", message="Die trofee wag vir die toegewysde PH se goedkeuring." });
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var caller = await _access.ResolveAsync(User, cancellationToken);
        if (caller.Can(MobileCapabilityNames.SubmitTrophy)) return Ok(await _repository.ListForHunterAsync(caller.UserId, cancellationToken));
        if (caller.Can(MobileCapabilityNames.ReviewTrophies)) return Ok(await _repository.ListForPhAsync(caller.UserRoleId, cancellationToken));
        return Forbid();
    }

    [HttpGet("{submissionId:long}")]
    public async Task<IActionResult> Get(long submissionId, CancellationToken cancellationToken)
    {
        var caller = await _access.ResolveAsync(User, cancellationToken);
        var record = await _repository.GetAsync(submissionId, cancellationToken);
        if (record is null) return NotFound();
        if (caller.Role == MobileRole.Hunter && record.Submission.HunterUserId == caller.UserId) return Ok(record.Submission);
        if (caller.Role == MobileRole.Ph)
        {
            var access = await _access.GetBookingAccessAsync(caller, record.Submission.BookingId, cancellationToken);
            if (access?.IsParticipant == true) return Ok(record.Submission);
        }
        return Forbid();
    }

    [HttpGet("{submissionId:long}/photo")]
    public async Task<IActionResult> Photo(long submissionId, CancellationToken cancellationToken)
    {
        var caller = await _access.ResolveAsync(User, cancellationToken);
        var record = await _repository.GetAsync(submissionId, cancellationToken);
        if (record is null) return NotFound();

        var mayView = caller.Role == MobileRole.Hunter &&
            record.Submission.HunterUserId == caller.UserId;
        if (!mayView && caller.Role == MobileRole.Ph && caller.Can(MobileCapabilityNames.ReviewTrophies))
        {
            var booking = await _access.GetBookingAccessAsync(
                caller,
                record.Submission.BookingId,
                cancellationToken);
            mayView = booking?.IsParticipant == true;
        }

        if (!mayView) return NotFound();
        if (!_photos.TryGetOwnedFile(
                record.Submission.TrophyPhotoStorageReference,
                record.Submission.HunterUserId,
                out var photo) || photo is null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return PhysicalFile(photo.FullPath, photo.ContentType, enableRangeProcessing: true);
    }

    [HttpPost("{submissionId:long}/review")]
    public async Task<IActionResult> Review(long submissionId, [FromBody] TrophyReviewRequest request, CancellationToken cancellationToken)
    {
        var caller = await _access.ResolveAsync(User, cancellationToken);
        if (!caller.Can(MobileCapabilityNames.ReviewTrophies) || caller.Role != MobileRole.Ph) return Forbid();
        var action = request.Action.Trim();
        if (action is not ("Approve" or "Reject" or "RequestCorrection")) return BadRequest(new { message = "Ongeldige hersieningsaksie." });
        if (action is "Reject" or "RequestCorrection" && string.IsNullOrWhiteSpace(request.Reason))
            return BadRequest(new { message = "'n Verduideliking is vir hierdie aksie verpligtend." });
        if (request.Reason?.Length > 1000) return BadRequest(new { message = "Die hersieningsnota is te lank." });
        if (request.ActionKey == Guid.Empty) return BadRequest(new { message = "'n Unieke aksiesleutel is verpligtend." });
        var record = await _repository.GetAsync(submissionId, cancellationToken);
        if (record is null) return NotFound();
        var booking = await _access.GetBookingAccessAsync(caller, record.Submission.BookingId, cancellationToken);
        if (booking?.IsEligible != true) return Forbid();
        RowlandWardEvaluationSnapshot? evaluation = null;
        if (action == "Approve" && record.RowlandWard is not null)
        {
            try
            {
                evaluation = _evaluation.Evaluate(record.RowlandWard);
                if (evaluation.QualificationState == RowlandWardQualificationStates.RequiredMeasurementsMissing)
                    return Conflict(new { message = "Die Rowland Ward-metings is onvolledig; versoek asseblief 'n regstelling." });
            }
            catch (RowlandWardEvaluationException exception) { return Conflict(new { message = exception.Message }); }
        }
        var outcome = await _repository.ReviewAsync(submissionId, caller, request, evaluation, cancellationToken);
        return outcome switch
        {
            TrophyReviewOutcome.Reviewed => Ok(new { status = action == "Approve" ? "Approved" : action == "Reject" ? "Rejected" : "CorrectionRequested" }),
            TrophyReviewOutcome.AlreadyReviewed => Conflict(new { message = "Hierdie indiening is reeds afgehandel." }),
            TrophyReviewOutcome.DuplicateAction => Conflict(new { message = "Hierdie hersieningsaksie is reeds verwerk." }),
            TrophyReviewOutcome.NotFound => NotFound(),
            _ => Forbid()
        };
    }

    [HttpPost("{submissionId:long}/resubmit")]
    public async Task<IActionResult> Resubmit(long submissionId, [FromBody] TrophyResubmitRequest request, CancellationToken cancellationToken)
    {
        var caller = await _access.ResolveAsync(User, cancellationToken);
        if (!caller.Can(MobileCapabilityNames.SubmitTrophy)) return Forbid();
        if (request.Notes?.Length > 4000 ||
            string.IsNullOrWhiteSpace(request.Notes) && string.IsNullOrWhiteSpace(request.TrophyPhotoPath))
            return BadRequest(new { message = "Voeg bygewerkte notas of 'n vervangingsfoto by." });
        if (!string.IsNullOrWhiteSpace(request.TrophyPhotoPath) && !_photos.IsOwnedPath(request.TrophyPhotoPath, caller.UserId))
            return BadRequest(new { message = "Die vervangingsfoto behoort nie aan jou veilige oplaai-area nie." });
        return await _repository.ResubmitAsync(submissionId, caller.UserId, request, cancellationToken)
            ? Ok(new { status="Resubmitted" })
            : Conflict(new { message = "Net 'n regstellingversoek wat aan jou behoort kan weer ingedien word." });
    }

    private static TrophySaveRequest ToSaveRequest(SubmitTrophyRequest request) => new()
    {
        Species=request.Species,PhotoPath=request.TrophyPhotoPath,AntlerSizeCm=request.AntlerSizeCm,CarcassWeightKg=request.CarcassWeightKg,
        ShotDistanceMeters=request.ShotDistanceMeters,FirearmType=request.FirearmType,HarvestDate=request.HarvestDate,
        LocationText=request.LocationText,Notes=request.Notes,IsPublic=false,RowlandWard=request.RowlandWard
    };
}
