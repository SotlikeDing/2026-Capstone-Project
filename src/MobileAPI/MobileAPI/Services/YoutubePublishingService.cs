using System.Data;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using MobileApi.Database;
using MobileApi.Models;
using MobileApi.Options;

namespace MobileApi.Services;

public sealed record YoutubeConnection(int Id, string ChannelId, string ChannelTitle, byte[] ProtectedRefreshToken);
public sealed record YoutubePublishJob(long JobId, long RecapId, int ConnectionId, string Status, int Attempts,
    string ExpectedPrivacy, string? VideoId, byte[]? ProtectedSessionUri);

public interface IYoutubePublishStore
{
    Task<long> QueueAsync(long recapId, int userId, Guid key, string privacy, CancellationToken cancellationToken);
    Task<YoutubePublishJob?> ClaimAsync(string workerId, int maxAttempts, CancellationToken cancellationToken);
    Task<YoutubeConnection?> GetConnectionAsync(int id, CancellationToken cancellationToken);
    Task SetSessionAsync(YoutubePublishJob job, byte[] protectedUri, CancellationToken cancellationToken);
    Task SetProgressAsync(YoutubePublishJob job, int progress, CancellationToken cancellationToken);
    Task SetVideoAsync(YoutubePublishJob job, string videoId, CancellationToken cancellationToken);
    Task SetProcessingAsync(YoutubePublishJob job, string processingStatus, TimeSpan retryAfter, CancellationToken cancellationToken);
    Task MarkPublishedAsync(YoutubePublishJob job, string videoId, string url, string privacy, string processingStatus,
        string channelId, DateTime uploadedAt, CancellationToken cancellationToken);
    Task MarkFailedAsync(YoutubePublishJob job, string status, string reason, DateTime? retryAt, CancellationToken cancellationToken);
    Task<bool> RetryAsync(long recapId, CancellationToken cancellationToken);
}

public sealed class YoutubePublishStore : IYoutubePublishStore
{
    private readonly string _connectionString;
    private readonly SharedBookingOptions _options;
    public YoutubePublishStore(IConfiguration configuration, IOptions<SharedBookingOptions> options)
    { _connectionString=configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Missing database connection."); _options=options.Value; }

    public async Task<long> QueueAsync(long recapId, int userId, Guid key, string privacy, CancellationToken cancellationToken)
    {
        const string sql = """
            SET XACT_ABORT ON; BEGIN TRANSACTION;
            DECLARE @Existing bigint=(SELECT youtubePublishJobId FROM dbo.youtubePublishJobs WHERE recapId=@RecapId);
            IF @Existing IS NOT NULL BEGIN COMMIT; SELECT @Existing; RETURN; END;
            IF EXISTS(SELECT 1 FROM dbo.youtubePublishJobs WHERE idempotencyKey=@Key)
                THROW 51103,'The publication idempotency key belongs to another recap.',1;
            IF NOT EXISTS(SELECT 1 FROM dbo.bookingRecaps WHERE recapId=@RecapId AND status='Completed' AND outputFileSize>0)
                THROW 51101,'Only a completed valid recap can be published.',1;
            DECLARE @Connection int=(SELECT TOP 1 youtubeChannelConnectionId FROM dbo.youtubeChannelConnections WHERE status='Active' ORDER BY connectedAt DESC);
            IF @Connection IS NULL THROW 51102,'The Mikpunt YouTube channel has not been connected by an administrator.',1;
            INSERT dbo.youtubePublishJobs(recapId,channelConnectionId,requestedByUserId,idempotencyKey,status,expectedPrivacyStatus)
            VALUES(@RecapId,@Connection,@UserId,@Key,'Queued',@Privacy);
            DECLARE @Id bigint=SCOPE_IDENTITY(); UPDATE dbo.bookingRecaps SET status='Uploading',uploadStatus='Queued' WHERE recapId=@RecapId;
            COMMIT; SELECT @Id;
            """;
        await using var connection=new SqlConnection(_connectionString); await using var command=new SqlCommand(sql,connection);
        command.Parameters.Add("@RecapId",SqlDbType.BigInt).Value=recapId; command.Parameters.Add("@UserId",SqlDbType.Int).Value=userId;
        command.Parameters.Add("@Key",SqlDbType.UniqueIdentifier).Value=key; command.Parameters.Add("@Privacy",SqlDbType.NVarChar,30).Value=privacy;
        await connection.OpenAsync(cancellationToken); return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<YoutubePublishJob?> ClaimAsync(string workerId, int maxAttempts, CancellationToken cancellationToken)
    {
        const string sql="""
            SET XACT_ABORT ON; BEGIN TRANSACTION;
            DECLARE @Id bigint=(SELECT TOP 1 youtubePublishJobId FROM dbo.youtubePublishJobs WITH(UPDLOCK,READPAST,ROWLOCK)
              WHERE status IN('Queued','RetryableFailure','Uploading','Uploaded','Processing') AND nextAttemptAt<=SYSUTCDATETIME()
                AND (youtubeVideoId IS NOT NULL OR attemptCount<@Max)
                AND (leaseExpiresAt IS NULL OR leaseExpiresAt<SYSUTCDATETIME()) ORDER BY nextAttemptAt,createdAt);
            IF @Id IS NOT NULL UPDATE dbo.youtubePublishJobs SET status=CASE WHEN youtubeVideoId IS NULL THEN 'Uploading' ELSE 'Processing' END,
              leaseOwner=@Worker,leaseExpiresAt=DATEADD(minute,15,SYSUTCDATETIME()),
              attemptCount=attemptCount+CASE WHEN status IN('Queued','RetryableFailure','Uploading') THEN 1 ELSE 0 END,
              updatedAt=SYSUTCDATETIME() WHERE youtubePublishJobId=@Id;
            COMMIT;
            SELECT youtubePublishJobId,recapId,channelConnectionId,status,attemptCount,expectedPrivacyStatus,youtubeVideoId,resumableSessionUriEncrypted
              FROM dbo.youtubePublishJobs WHERE youtubePublishJobId=@Id;
            """;
        await using var connection=new SqlConnection(_connectionString); await using var command=new SqlCommand(sql,connection);
        command.Parameters.Add("@Max",SqlDbType.Int).Value=maxAttempts; command.Parameters.Add("@Worker",SqlDbType.NVarChar,120).Value=workerId;
        await connection.OpenAsync(cancellationToken); await using var reader=await command.ExecuteReaderAsync(cancellationToken);
        if(!await reader.ReadAsync(cancellationToken)) return null;
        return new YoutubePublishJob(reader.GetInt64(0),reader.GetInt64(1),reader.GetInt32(2),reader.GetString(3),reader.GetInt32(4),reader.GetString(5),reader.IsDBNull(6)?null:reader.GetString(6),reader.IsDBNull(7)?null:(byte[])reader[7]);
    }

    public async Task<YoutubeConnection?> GetConnectionAsync(int id, CancellationToken cancellationToken)
    {
        const string sql="SELECT youtubeChannelConnectionId,channelId,channelTitle,encryptedRefreshToken FROM dbo.youtubeChannelConnections WHERE youtubeChannelConnectionId=@Id AND status='Active';";
        await using var connection=new SqlConnection(_connectionString); await using var command=new SqlCommand(sql,connection); command.Parameters.Add("@Id",SqlDbType.Int).Value=id;
        await connection.OpenAsync(cancellationToken); await using var reader=await command.ExecuteReaderAsync(cancellationToken); return await reader.ReadAsync(cancellationToken)?new YoutubeConnection(reader.GetInt32(0),reader.GetString(1),reader.GetString(2),(byte[])reader[3]):null;
    }

    public Task SetSessionAsync(YoutubePublishJob job, byte[] protectedUri, CancellationToken cancellationToken)=>Exec("""
        UPDATE dbo.youtubePublishJobs SET resumableSessionUriEncrypted=@Bytes,status='Uploading',updatedAt=SYSUTCDATETIME() WHERE youtubePublishJobId=@Id;
        UPDATE dbo.bookingRecaps SET uploadStatus='Uploading' WHERE recapId=@RecapId;
        """,job,cancellationToken,bytes:protectedUri);
    public Task SetProgressAsync(YoutubePublishJob job,int progress,CancellationToken cancellationToken)=>Exec("UPDATE dbo.youtubePublishJobs SET uploadProgress=@Progress,leaseExpiresAt=DATEADD(minute,15,SYSUTCDATETIME()),updatedAt=SYSUTCDATETIME() WHERE youtubePublishJobId=@Id;",job,cancellationToken,progress:progress);
    public Task SetVideoAsync(YoutubePublishJob job,string videoId,CancellationToken cancellationToken)=>Exec("""
        UPDATE dbo.youtubePublishJobs SET youtubeVideoId=@Video,status='Uploaded',uploadProgress=100,nextAttemptAt=DATEADD(second,20,SYSUTCDATETIME()),leaseOwner=NULL,leaseExpiresAt=NULL,updatedAt=SYSUTCDATETIME() WHERE youtubePublishJobId=@Id;
        UPDATE dbo.bookingRecaps SET youtubeVideoId=@Video,uploadStatus='Uploaded',youtubeProcessingStatus='uploaded' WHERE recapId=@RecapId;
        """,job,cancellationToken,video:videoId);
    public Task SetProcessingAsync(YoutubePublishJob job,string processingStatus,TimeSpan retryAfter,CancellationToken cancellationToken)=>Exec("""
        UPDATE dbo.youtubePublishJobs SET status='Processing',processingStatus=@Processing,nextAttemptAt=DATEADD(second,@Delay,SYSUTCDATETIME()),leaseOwner=NULL,leaseExpiresAt=NULL,updatedAt=SYSUTCDATETIME() WHERE youtubePublishJobId=@Id;
        UPDATE dbo.bookingRecaps SET uploadStatus='Processing',youtubeProcessingStatus=@Processing WHERE recapId=@RecapId;
        """,job,cancellationToken,processing:processingStatus,delay:(int)retryAfter.TotalSeconds);

    public async Task MarkPublishedAsync(YoutubePublishJob job,string videoId,string url,string privacy,string processingStatus,string channelId,DateTime uploadedAt,CancellationToken cancellationToken)
    {
        const string sql="""
            SET XACT_ABORT ON; BEGIN TRANSACTION;
            UPDATE dbo.youtubePublishJobs SET status='Published',youtubeVideoId=@Video,processingStatus=@Processing,uploadProgress=100,
              verifiedAt=SYSUTCDATETIME(),leaseOwner=NULL,leaseExpiresAt=NULL,updatedAt=SYSUTCDATETIME() WHERE youtubePublishJobId=@Id;
            UPDATE dbo.bookingRecaps SET status='Published',uploadStatus='Published',youtubeVideoId=@Video,youtubeUrl=@Url,
              youtubePrivacyStatus=@Privacy,youtubeProcessingStatus=@Processing,deletionStatus='CleanupPending' WHERE recapId=@RecapId;
            INSERT dbo.bookingVideoArchives(bookingId,recapId,youtubeVideoId,youtubeUrl,youtubeChannelId,title,privacyStatus,uploadedAt,verifiedAt,
              sourceMediaCount,hunterPhotoCount,phPhotoCount,vgPhotoCount,sourceRouteId,generatedVideoDurationSeconds,generatedVideoSize,archiveVersion)
            SELECT r.bookingId,r.recapId,@Video,@Url,@Channel,r.title,@Privacy,@Uploaded,SYSUTCDATETIME(),
              COUNT(m.mediaId),COALESCE(SUM(CASE WHEN m.uploaderRole='Hunter' THEN 1 ELSE 0 END),0),COALESCE(SUM(CASE WHEN m.uploaderRole='PH' THEN 1 ELSE 0 END),0),COALESCE(SUM(CASE WHEN m.uploaderRole='VG' THEN 1 ELSE 0 END),0),
              r.selectedRouteId,r.durationSeconds,r.outputFileSize,r.version FROM dbo.bookingRecaps r
              LEFT JOIN dbo.bookingMedia m ON m.bookingId=r.bookingId AND m.deletedAt IS NULL AND m.inclusionStatus='Included' AND m.mediaVersion<=r.sourceMediaVersion
              WHERE r.recapId=@RecapId GROUP BY r.bookingId,r.recapId,r.title,r.selectedRouteId,r.durationSeconds,r.outputFileSize,r.version;
            DECLARE @Archive bigint=(SELECT archiveId FROM dbo.bookingVideoArchives WHERE recapId=@RecapId);
            INSERT dbo.mediaCleanupJobs(bookingId,recapId,archiveId,idempotencyKey,status,notBefore)
              SELECT bookingId,recapId,@Archive,NEWID(),'CleanupPending',DATEADD(hour,@Retention,SYSUTCDATETIME()) FROM dbo.bookingRecaps WHERE recapId=@RecapId;
            COMMIT;
            """;
        await using var connection=new SqlConnection(_connectionString); await using var command=new SqlCommand(sql,connection);
        command.Parameters.Add("@Id",SqlDbType.BigInt).Value=job.JobId;command.Parameters.Add("@RecapId",SqlDbType.BigInt).Value=job.RecapId;
        command.Parameters.Add("@Video",SqlDbType.NVarChar,40).Value=videoId;command.Parameters.Add("@Url",SqlDbType.NVarChar,500).Value=url;
        command.Parameters.Add("@Privacy",SqlDbType.NVarChar,30).Value=privacy;command.Parameters.Add("@Processing",SqlDbType.NVarChar,60).Value=processingStatus;
        command.Parameters.Add("@Channel",SqlDbType.NVarChar,80).Value=channelId;command.Parameters.Add("@Uploaded",SqlDbType.DateTime2).Value=uploadedAt;
        command.Parameters.Add("@Retention",SqlDbType.Int).Value=_options.CleanupRetentionHours;
        await connection.OpenAsync(cancellationToken);await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public Task MarkFailedAsync(YoutubePublishJob job,string status,string reason,DateTime? retryAt,CancellationToken cancellationToken)=>Exec("""
        UPDATE dbo.youtubePublishJobs SET status=@Status,lastError=@Error,nextAttemptAt=COALESCE(@Retry,SYSUTCDATETIME()),leaseOwner=NULL,leaseExpiresAt=NULL,updatedAt=SYSUTCDATETIME() WHERE youtubePublishJobId=@Id;
        UPDATE dbo.bookingRecaps SET status=CASE WHEN @Status='RetryableFailure' THEN 'Uploading' ELSE 'Completed' END,uploadStatus=@Status,failureReason=@Error WHERE recapId=@RecapId;
        """,job,cancellationToken,status:status,error:reason,retry:retryAt);

    public async Task<bool> RetryAsync(long recapId,CancellationToken cancellationToken)
    {
        const string sql="""
            UPDATE dbo.youtubePublishJobs SET status=CASE WHEN youtubeVideoId IS NULL THEN 'Queued' ELSE 'Processing' END,
              nextAttemptAt=SYSUTCDATETIME(),lastError=NULL,leaseOwner=NULL,leaseExpiresAt=NULL,updatedAt=SYSUTCDATETIME()
            WHERE recapId=@Recap AND status IN('Failed','RetryableFailure','VerificationFailed');
            UPDATE dbo.bookingRecaps SET status='Uploading',uploadStatus='Queued',failureReason=NULL WHERE recapId=@Recap AND @@ROWCOUNT=1;
            SELECT @@ROWCOUNT;
            """;
        await using var c=new SqlConnection(_connectionString);await using var cmd=new SqlCommand(sql,c);cmd.Parameters.Add("@Recap",SqlDbType.BigInt).Value=recapId;await c.OpenAsync(cancellationToken);return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken))==1;
    }

    private async Task Exec(string sql,YoutubePublishJob job,CancellationToken cancellationToken,int? progress=null,byte[]? bytes=null,string? video=null,string? processing=null,int? delay=null,string? status=null,string? error=null,DateTime? retry=null)
    {
        await using var connection=new SqlConnection(_connectionString);await using var command=new SqlCommand(sql,connection);
        command.Parameters.Add("@Id",SqlDbType.BigInt).Value=job.JobId;command.Parameters.Add("@RecapId",SqlDbType.BigInt).Value=job.RecapId;
        if(sql.Contains("@Progress"))command.Parameters.Add("@Progress",SqlDbType.Int).Value=progress;
        if(sql.Contains("@Bytes"))command.Parameters.Add("@Bytes",SqlDbType.VarBinary,-1).Value=bytes!;
        if(sql.Contains("@Video"))command.Parameters.Add("@Video",SqlDbType.NVarChar,40).Value=video!;
        if(sql.Contains("@Processing"))command.Parameters.Add("@Processing",SqlDbType.NVarChar,60).Value=processing!;
        if(sql.Contains("@Delay"))command.Parameters.Add("@Delay",SqlDbType.Int).Value=delay;
        if(sql.Contains("@Status"))command.Parameters.Add("@Status",SqlDbType.NVarChar,30).Value=status!;
        if(sql.Contains("@Error"))command.Parameters.Add("@Error",SqlDbType.NVarChar,1000).Value=error!;
        if(sql.Contains("@Retry"))command.Parameters.Add("@Retry",SqlDbType.DateTime2).Value=(object?)retry??DBNull.Value;
        await connection.OpenAsync(cancellationToken);await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

public sealed class YoutubeOAuthService
{
    private readonly HttpClient _http;
    private readonly YoutubeOptions _options;
    private readonly IDataProtector _tokens;
    private readonly string _connectionString;
    public YoutubeOAuthService(HttpClient http,IOptions<YoutubeOptions> options,IDataProtectionProvider protection,IConfiguration configuration)
    { _http=http;_options=options.Value;_tokens=protection.CreateProtector(_options.DataProtectionPurpose);_connectionString=configuration.GetConnectionString("DefaultConnection")!; }
    public bool IsConfigured=>!string.IsNullOrWhiteSpace(_options.ClientId)&&!string.IsNullOrWhiteSpace(_options.ClientSecret)&&Uri.TryCreate(_options.RedirectUri,UriKind.Absolute,out _);
    public string BuildAuthorizationUrl(string state)=>"https://accounts.google.com/o/oauth2/v2/auth?"+string.Join('&',new Dictionary<string,string>{["client_id"]=_options.ClientId,["redirect_uri"]=_options.RedirectUri,["response_type"]="code",["scope"]="https://www.googleapis.com/auth/youtube.upload https://www.googleapis.com/auth/youtube.readonly",["access_type"]="offline",["include_granted_scopes"]="true",["prompt"]="consent",["state"]=state}.Select(x=>$"{WebUtility.UrlEncode(x.Key)}={WebUtility.UrlEncode(x.Value)}"));
    public string Protect(string value)=>Convert.ToBase64String(_tokens.Protect(Encoding.UTF8.GetBytes(value)));
    public string Unprotect(string value)=>Encoding.UTF8.GetString(_tokens.Unprotect(Convert.FromBase64String(value)));
    public byte[] ProtectToken(string value)=>_tokens.Protect(Encoding.UTF8.GetBytes(value));
    public string UnprotectToken(byte[] value)=>Encoding.UTF8.GetString(_tokens.Unprotect(value));

    public async Task<YoutubeConnection> CompleteAsync(string code,int adminUserId,CancellationToken cancellationToken)
    {
        using var tokenResponse=await _http.PostAsync("https://oauth2.googleapis.com/token",new FormUrlEncodedContent(new Dictionary<string,string>{{"code",code},{"client_id",_options.ClientId},{"client_secret",_options.ClientSecret},{"redirect_uri",_options.RedirectUri},{"grant_type","authorization_code"}}),cancellationToken);
        var body=await tokenResponse.Content.ReadAsStringAsync(cancellationToken);if(!tokenResponse.IsSuccessStatusCode)throw new InvalidOperationException("Google OAuth token exchange failed.");
        using var document=JsonDocument.Parse(body);var root=document.RootElement;var access=root.GetProperty("access_token").GetString()!;
        if(!root.TryGetProperty("refresh_token",out var refreshElement)||string.IsNullOrWhiteSpace(refreshElement.GetString()))throw new InvalidOperationException("Google did not return a refresh token. Revoke the prior grant and reconnect with consent.");
        var channel=await GetChannelAsync(access,cancellationToken);var protectedRefresh=ProtectToken(refreshElement.GetString()!);
        const string sql="""
            UPDATE dbo.youtubeChannelConnections SET status='Revoked',revokedAt=SYSUTCDATETIME() WHERE status='Active' AND channelId<>@Channel;
            MERGE dbo.youtubeChannelConnections AS target USING(SELECT @Channel AS channelId) source ON target.channelId=source.channelId
            WHEN MATCHED THEN UPDATE SET channelTitle=@Title,encryptedRefreshToken=@Token,tokenProtectionKeyId=@Purpose,connectedByUserId=@User,status='Active',connectedAt=SYSUTCDATETIME(),lastVerifiedAt=SYSUTCDATETIME(),revokedAt=NULL
            WHEN NOT MATCHED THEN INSERT(channelId,channelTitle,encryptedRefreshToken,tokenProtectionKeyId,connectedByUserId,lastVerifiedAt)VALUES(@Channel,@Title,@Token,@Purpose,@User,SYSUTCDATETIME());
            SELECT youtubeChannelConnectionId,channelId,channelTitle,encryptedRefreshToken FROM dbo.youtubeChannelConnections WHERE channelId=@Channel;
            """;
        await using var connection=new SqlConnection(_connectionString);await using var command=new SqlCommand(sql,connection);
        command.Parameters.Add("@Channel",SqlDbType.NVarChar,80).Value=channel.Id;command.Parameters.Add("@Title",SqlDbType.NVarChar,180).Value=channel.Title;
        command.Parameters.Add("@Token",SqlDbType.VarBinary,-1).Value=protectedRefresh;command.Parameters.Add("@Purpose",SqlDbType.NVarChar,120).Value=_options.DataProtectionPurpose;command.Parameters.Add("@User",SqlDbType.Int).Value=adminUserId;
        await connection.OpenAsync(cancellationToken);await using var reader=await command.ExecuteReaderAsync(cancellationToken);await reader.ReadAsync(cancellationToken);
        return new YoutubeConnection(reader.GetInt32(0),reader.GetString(1),reader.GetString(2),(byte[])reader[3]);
    }
    /// <summary>Retires a connection whose stored token can no longer be decrypted; reconnecting the channel makes it Active again.</summary>
    public async Task MarkConnectionUnusableAsync(int connectionId,CancellationToken cancellationToken)
    {
        await using var connection=new SqlConnection(_connectionString);
        await using var command=new SqlCommand("UPDATE dbo.youtubeChannelConnections SET status='Revoked',revokedAt=SYSUTCDATETIME() WHERE youtubeChannelConnectionId=@Id AND status='Active';",connection);
        command.Parameters.Add("@Id",SqlDbType.Int).Value=connectionId;
        await connection.OpenAsync(cancellationToken);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
    public async Task<string> RefreshAccessTokenAsync(byte[] protectedRefresh,CancellationToken cancellationToken)
    {
        var refresh=UnprotectToken(protectedRefresh);using var response=await _http.PostAsync("https://oauth2.googleapis.com/token",new FormUrlEncodedContent(new Dictionary<string,string>{{"client_id",_options.ClientId},{"client_secret",_options.ClientSecret},{"refresh_token",refresh},{"grant_type","refresh_token"}}),cancellationToken);
        var body=await response.Content.ReadAsStringAsync(cancellationToken);if(!response.IsSuccessStatusCode)throw new InvalidOperationException("YouTube access token refresh failed.");using var doc=JsonDocument.Parse(body);return doc.RootElement.GetProperty("access_token").GetString()!;
    }
    private async Task<(string Id,string Title)> GetChannelAsync(string access,CancellationToken cancellationToken)
    { using var request=new HttpRequestMessage(HttpMethod.Get,"https://www.googleapis.com/youtube/v3/channels?part=snippet&mine=true");request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",access);using var response=await _http.SendAsync(request,cancellationToken);var body=await response.Content.ReadAsStringAsync(cancellationToken);if(!response.IsSuccessStatusCode)throw new InvalidOperationException("The connected Google account has no readable YouTube channel.");using var doc=JsonDocument.Parse(body);var items=doc.RootElement.GetProperty("items");if(items.GetArrayLength()!=1)throw new InvalidOperationException("Exactly one YouTube channel must be selected.");var item=items[0];return(item.GetProperty("id").GetString()!,item.GetProperty("snippet").GetProperty("title").GetString()!); }
}

public sealed class YoutubePublishWorker:BackgroundService
{
    private readonly IServiceScopeFactory _scopes;private readonly ILogger<YoutubePublishWorker> _logger;private readonly SharedBookingOptions _limits;private readonly string _worker=$"{Environment.MachineName}-{Guid.NewGuid():N}";
    public YoutubePublishWorker(IServiceScopeFactory scopes,ILogger<YoutubePublishWorker> logger,IOptions<SharedBookingOptions> limits){_scopes=scopes;_logger=logger;_limits=limits.Value;}
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        while(!token.IsCancellationRequested)try
        {using var scope=_scopes.CreateScope();var store=scope.ServiceProvider.GetRequiredService<IYoutubePublishStore>();var job=await store.ClaimAsync(_worker,_limits.MaxYoutubeRetryAttempts,token);if(job is null){await Task.Delay(5000,token);continue;}try{await Process(scope.ServiceProvider,store,job,token);}
        catch(System.Security.Cryptography.CryptographicException)
        {
            // The connection's refresh token was encrypted with a data-protection key this server no longer has
            // (e.g. a container recreated before the key folder was a volume). Retrying can't help: mark the
            // connection as needing a reconnect and fail the job with an explanation, once.
            _logger.LogWarning("YouTube job {JobId}: the channel connection {ConnectionId} can't be decrypted any more - an admin must reconnect YouTube.",job.JobId,job.ConnectionId);
            await scope.ServiceProvider.GetRequiredService<YoutubeOAuthService>().MarkConnectionUnusableAsync(job.ConnectionId,token);
            await store.MarkFailedAsync(job,"PermanentlyFailed","Die YouTube-kanaal moet deur 'n administrateur herkoppel word.",null,token);
        }
        catch(Exception e)when(e is not OperationCanceledException){_logger.LogError(e,"YouTube job {JobId} failed",job.JobId);var retry=job.Attempts<_limits.MaxYoutubeRetryAttempts;await store.MarkFailedAsync(job,retry?"RetryableFailure":"PermanentlyFailed",SafeError(e.Message),retry?DateTime.UtcNow.AddMinutes(Math.Min(30,Math.Pow(2,job.Attempts))):null,token);}}
        catch(OperationCanceledException)when(token.IsCancellationRequested){}catch(Exception e){_logger.LogError(e,"YouTube worker loop failed");await Task.Delay(10000,token);}
    }
    private static async Task Process(IServiceProvider services,IYoutubePublishStore store,YoutubePublishJob job,CancellationToken token)
    {
        var oauth=services.GetRequiredService<YoutubeOAuthService>();var connection=await store.GetConnectionAsync(job.ConnectionId,token)??throw new InvalidOperationException("The YouTube channel connection is unavailable.");
        var access=await oauth.RefreshAccessTokenAsync(connection.ProtectedRefreshToken,token);var recap=await services.GetRequiredService<ISharedBookingRepository>().GetRecapAsync(job.RecapId,token)??throw new InvalidOperationException("Recap not found.");
        var storage=services.GetRequiredService<ISharedBookingStorage>();if(!storage.TryGetRecap(recap.OutputPrivateStorageReference,recap.BookingId,out var file)||file is null||file.Length<=0)throw new InvalidOperationException("The private recap file is unavailable.");
        var client=services.GetRequiredService<IHttpClientFactory>().CreateClient("youtube");var videoId=job.VideoId;
        if(string.IsNullOrWhiteSpace(videoId))videoId=await Upload(client,oauth,store,job,connection,access,recap,file,token);
        await Verify(client,store,job,connection,access,recap,videoId!,token);
    }
    private static async Task<string> Upload(
        HttpClient client,
        YoutubeOAuthService oauth,
        IYoutubePublishStore store,
        YoutubePublishJob job,
        YoutubeConnection connection,
        string access,
        BookingRecapDto recap,
        PrivateStorageFile file,
        CancellationToken token)
    {
        _ = connection;
        Uri? session = null;
        long offset = 0;

        if (job.ProtectedSessionUri is not null)
        {
            session = new Uri(oauth.UnprotectToken(job.ProtectedSessionUri));
            var resume = await QueryUploadAsync(client, session, file.Length, token);
            if (resume.VideoId is not null)
            {
                await store.SetVideoAsync(job, resume.VideoId, token);
                return resume.VideoId;
            }
            if (resume.SessionExpired)
            {
                session = null;
            }
            else
            {
                offset = resume.Offset;
            }
        }

        if (session is null)
        {
            var metadata = JsonSerializer.Serialize(new
            {
                snippet = new
                {
                    title = recap.Title,
                    description = "Mikpunt shared booking recap. Exact hunting coordinates are intentionally omitted."
                },
                status = new { privacyStatus = job.ExpectedPrivacy, selfDeclaredMadeForKids = false }
            }, JsonDefaults.Options);
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                "https://www.googleapis.com/upload/youtube/v3/videos?uploadType=resumable&part=snippet,status");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
            request.Headers.Add("X-Upload-Content-Length", file.Length.ToString(CultureInfo.InvariantCulture));
            request.Headers.Add("X-Upload-Content-Type", "video/mp4");
            request.Content = new StringContent(metadata, Encoding.UTF8, "application/json");
            using var response = await client.SendAsync(request, token);
            if (!response.IsSuccessStatusCode || response.Headers.Location is null)
                throw new InvalidOperationException("YouTube did not create a resumable upload session.");
            session = response.Headers.Location;
            await store.SetSessionAsync(job, oauth.ProtectToken(session.ToString()), token);
        }

        const int chunkSize = 8 * 1024 * 1024;
        await using var stream = new FileStream(
            file.FullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            chunkSize,
            useAsync: true);
        if (offset < 0 || offset > stream.Length)
            throw new InvalidOperationException("YouTube returned an invalid resumable upload offset.");
        stream.Position = offset;
        var buffer = new byte[chunkSize];
        while (offset < stream.Length)
        {
            var read = await stream.ReadAsync(
                buffer.AsMemory(0, (int)Math.Min(buffer.Length, stream.Length - offset)),
                token);
            if (read <= 0) break;
            using var request = new HttpRequestMessage(HttpMethod.Put, session);
            request.Content = new ByteArrayContent(buffer, 0, read);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
            request.Content.Headers.ContentRange = new ContentRangeHeaderValue(
                offset,
                offset + read - 1,
                stream.Length);
            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                token);
            var body = await response.Content.ReadAsStringAsync(token);
            if (response.StatusCode == (HttpStatusCode)308)
            {
                offset = ReadNextOffset(response.Headers, offset + read);
                stream.Position = offset;
                await store.SetProgressAsync(job, (int)(offset * 100 / stream.Length), token);
                continue;
            }
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"YouTube resumable upload failed with {(int)response.StatusCode}.");
            var id = ReadVideoId(body);
            await store.SetVideoAsync(job, id, token);
            return id;
        }
        throw new InvalidOperationException("YouTube upload ended without assigning a video id.");
    }

    internal static async Task<(long Offset, string? VideoId, bool SessionExpired)> QueryUploadAsync(
        HttpClient client,
        Uri session,
        long length,
        CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, session);
        request.Content = new ByteArrayContent([]);
        request.Content.Headers.ContentLength = 0;
        request.Content.Headers.ContentRange = new ContentRangeHeaderValue(length);
        using var response = await client.SendAsync(request, token);
        var body = await response.Content.ReadAsStringAsync(token);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
            return (0, null, true);
        if (response.StatusCode == (HttpStatusCode)308)
        {
            return (ReadNextOffset(response.Headers, 0), null, false);
        }
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"YouTube upload status query failed with {(int)response.StatusCode}.");
        return (length, ReadVideoId(body), false);
    }

    private static long ReadNextOffset(HttpResponseHeaders headers, long fallback)
    {
        if (!headers.TryGetValues("Range", out var values)) return fallback;
        var match = Regex.Match(values.FirstOrDefault() ?? string.Empty, @"bytes=\d+-(\d+)");
        return match.Success && long.TryParse(match.Groups[1].Value, out var finalByte)
            ? finalByte + 1
            : fallback;
    }

    private static string ReadVideoId(string body)
    {
        using var document = JsonDocument.Parse(body);
        var id = document.RootElement.GetProperty("id").GetString();
        return string.IsNullOrWhiteSpace(id)
            ? throw new InvalidOperationException("YouTube completed upload without a video id.")
            : id;
    }
    private static async Task Verify(HttpClient client,IYoutubePublishStore store,YoutubePublishJob job,YoutubeConnection connection,string access,BookingRecapDto recap,string videoId,CancellationToken token)
    {
        using var request=new HttpRequestMessage(HttpMethod.Get,$"https://www.googleapis.com/youtube/v3/videos?part=snippet,status,contentDetails&id={WebUtility.UrlEncode(videoId)}");request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",access);using var response=await client.SendAsync(request,token);if(!response.IsSuccessStatusCode)throw new InvalidOperationException("YouTube verification request failed.");using var doc=JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));var items=doc.RootElement.GetProperty("items");if(items.GetArrayLength()!=1){await store.MarkFailedAsync(job,"VerificationFailed","The uploaded video could not be retrieved from YouTube.",DateTime.UtcNow.AddMinutes(2),token);return;}var item=items[0];var snippet=item.GetProperty("snippet");var status=item.GetProperty("status");var channelId=snippet.GetProperty("channelId").GetString();var privacy=status.GetProperty("privacyStatus").GetString();var upload=status.GetProperty("uploadStatus").GetString();var title=snippet.GetProperty("title").GetString();var description=snippet.GetProperty("description").GetString();var duration=item.GetProperty("contentDetails").GetProperty("duration").GetString();var uploadedAt=snippet.TryGetProperty("publishedAt",out var published)&&published.TryGetDateTime(out var parsedPublished)?parsedPublished.ToUniversalTime():DateTime.UtcNow;
        if(channelId!=connection.ChannelId){await store.MarkFailedAsync(job,"VerificationFailed","The video was uploaded to an unexpected YouTube channel.",null,token);return;}if(privacy!=job.ExpectedPrivacy){await store.MarkFailedAsync(job,"VerificationFailed","The uploaded video's privacy status does not match the requested status.",null,token);return;}if(upload is "failed" or "rejected" or "deleted"){await store.MarkFailedAsync(job,"PermanentlyFailed",$"YouTube processing ended with status {upload}.",null,token);return;}if(upload!="processed"){await store.SetProcessingAsync(job,upload??"processing",TimeSpan.FromMinutes(1),token);return;}if(title!=recap.Title||description?.Contains("Exact hunting coordinates are intentionally omitted.",StringComparison.Ordinal)!=true||!HasPositiveDuration(duration)){await store.MarkFailedAsync(job,"VerificationFailed","YouTube returned unexpected title, description, or duration metadata.",DateTime.UtcNow.AddMinutes(2),token);return;}await store.MarkPublishedAsync(job,videoId,$"https://youtu.be/{videoId}",privacy!,upload,channelId!,uploadedAt,token);
    }
    private static bool HasPositiveDuration(string? value)
    {
        if(string.IsNullOrWhiteSpace(value))return false;
        try{return System.Xml.XmlConvert.ToTimeSpan(value)>TimeSpan.Zero;}
        catch(FormatException){return false;}
    }
    private static string SafeError(string value)
    {
        var redacted=Regex.Replace(value,@"(?i)(access|refresh|bearer|token|secret|password)\s*[:=]\s*\S+","[credential redacted]");
        return redacted.Length>900?redacted[..900]:redacted;
    }
}
