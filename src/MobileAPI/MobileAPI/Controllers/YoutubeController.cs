using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MobileApi.Database;
using MobileApi.Models;
using MobileApi.Services;

namespace MobileApi.Controllers;

[ApiController]
public sealed class YoutubeAdminController:ControllerBase
{
    private readonly IMobileAccessService _access;private readonly YoutubeOAuthService _oauth;
    public YoutubeAdminController(IMobileAccessService access,YoutubeOAuthService oauth){_access=access;_oauth=oauth;}

    [Authorize]
    [HttpGet("api/admin/youtube/oauth/start")]
    public async Task<IActionResult> Start(CancellationToken cancellationToken)
    {
        var caller=await _access.ResolveAsync(User,cancellationToken);var normalized=new string(caller.RoleCode.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        if(normalized is not ("ADMIN" or "ADMINISTRATOR"))return Forbid();
        if(!_oauth.IsConfigured)return StatusCode(503,new{message="YouTube OAuth client configuration is not installed on this server."});
        var state=_oauth.Protect(JsonSerializer.Serialize(new{userId=caller.UserId,expiresAt=DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds(),nonce=Guid.NewGuid()}));
        return Ok(new{authorizationUrl=_oauth.BuildAuthorizationUrl(state),expiresInSeconds=600});
    }

    [AllowAnonymous]
    [HttpGet("api/admin/youtube/oauth/callback")]
    public async Task<IActionResult> Callback([FromQuery]string? code,[FromQuery]string? state,[FromQuery]string? error,CancellationToken cancellationToken)
    {
        if(!string.IsNullOrWhiteSpace(error))return BadRequest("Google authorization was not completed.");
        if(string.IsNullOrWhiteSpace(code)||string.IsNullOrWhiteSpace(state))return BadRequest("The OAuth callback is incomplete.");
        try
        {
            using var document=JsonDocument.Parse(_oauth.Unprotect(state));var root=document.RootElement;var userId=root.GetProperty("userId").GetInt32();var expires=root.GetProperty("expiresAt").GetInt64();
            if(expires<DateTimeOffset.UtcNow.ToUnixTimeSeconds())return BadRequest("The OAuth connection request has expired.");
            var connection=await _oauth.CompleteAsync(code,userId,cancellationToken);
            return Content($"Mikpunt is securely connected to YouTube channel ‘{connection.ChannelTitle}’. You may close this page.","text/plain");
        }
        catch(Exception){return BadRequest("The YouTube channel connection could not be verified. No password was stored.");}
    }
}

[ApiController]
[Authorize]
[Route("api/bookings/{bookingId:int}/recaps/{recapId:long}")]
public sealed class YoutubePublishingController:ControllerBase
{
    private readonly IMobileAccessService _access;private readonly ISharedBookingRepository _recaps;private readonly IYoutubePublishStore _publishing;private readonly IMediaCleanupStore _cleanup;
    public YoutubePublishingController(IMobileAccessService access,ISharedBookingRepository recaps,IYoutubePublishStore publishing,IMediaCleanupStore cleanup)
    {_access=access;_recaps=recaps;_publishing=publishing;_cleanup=cleanup;}

    [HttpPost("publish")]
    public async Task<IActionResult> Publish(int bookingId,long recapId,[FromBody]PublishRecapRequest request,CancellationToken cancellationToken)
    {
        var (caller,error)=await Authorize(bookingId,recapId,MobileCapabilityNames.PublishRecap,cancellationToken);if(error is not null)return error;
        if(request.IdempotencyKey==Guid.Empty)return BadRequest(new{message="'n Unieke publikasiesleutel is verpligtend."});
        var privacy=request.PrivacyStatus.Trim().ToLowerInvariant();if(privacy!="private")return BadRequest(new{message="YouTube-video's moet tans privaat gepubliseer word."});
        try{var jobId=await _publishing.QueueAsync(recapId,caller!.UserId,request.IdempotencyKey,privacy,cancellationToken);return Accepted(new{publishJobId=jobId,status="Queued",message="YouTube-oplaai is veilig in die ry geplaas."});}
        catch(Microsoft.Data.SqlClient.SqlException e)when(e.Number is 51101 or 51102 or 51103){return Conflict(new{message=e.Message});}
    }

    [HttpPost("publish/retry")]
    public async Task<IActionResult> Retry(int bookingId,long recapId,CancellationToken cancellationToken)
    {var(_,error)=await Authorize(bookingId,recapId,MobileCapabilityNames.PublishRecap,cancellationToken);if(error is not null)return error;return await _publishing.RetryAsync(recapId,cancellationToken)?Accepted(new{status="Queued"}):Conflict(new{message="Hierdie publikasie kan nie veilig weer probeer word nie."});}

    [HttpPost("cleanup")]
    public async Task<IActionResult> Cleanup(int bookingId,long recapId,[FromBody]CleanupRequest request,CancellationToken cancellationToken)
    {var(caller,error)=await Authorize(bookingId,recapId,MobileCapabilityNames.PublishRecap,cancellationToken);if(error is not null)return error;if(!request.ConfirmImmediateCleanup)return BadRequest(new{message="Bevestig uitdruklik onmiddellike skoonmaak, of wag vir die veiligheidsretensietyd."});return await _cleanup.RequestImmediateAsync(recapId,caller!.UserId,cancellationToken)?Accepted(new{status="CleanupPending"}):Conflict(new{message="Skoonmaak is eers beskikbaar nadat YouTube-publikasie onafhanklik geverifieer en geargiveer is."});}

    [HttpGet("cleanup")]
    public async Task<IActionResult> CleanupStatus(int bookingId,long recapId,CancellationToken cancellationToken)
    {var(_,error)=await Authorize(bookingId,recapId,MobileCapabilityNames.ViewRecap,cancellationToken);if(error is not null)return error;var value=await _cleanup.GetStatusAsync(recapId,cancellationToken);return value is null?NotFound():Ok(value);}

    private async Task<(MobileRoleContext?,IActionResult?)> Authorize(int bookingId,long recapId,string capability,CancellationToken token)
    {var caller=await _access.ResolveAsync(User,token);if(!caller.Can(capability))return(caller,Forbid());var booking=await _access.GetBookingAccessAsync(caller,bookingId,token);if(booking?.IsParticipant!=true)return(caller,Forbid());var recap=await _recaps.GetRecapAsync(recapId,token);if(recap is null||recap.BookingId!=bookingId)return(caller,NotFound());return(caller,null);}
}
