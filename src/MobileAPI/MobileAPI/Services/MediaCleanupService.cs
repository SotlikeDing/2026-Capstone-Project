using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace MobileApi.Services;

public sealed record CleanupJob(long JobId,int BookingId,long RecapId,long ArchiveId,int SourceMediaVersion,string? RecapReference,List<(long MediaId,string Reference)> Media);

public interface IMediaCleanupStore
{
    Task<CleanupJob?> ClaimAsync(string workerId,CancellationToken cancellationToken);
    Task<bool> RequestImmediateAsync(long recapId,int requestedByUserId,CancellationToken cancellationToken);
    Task<object?> GetStatusAsync(long recapId,CancellationToken cancellationToken);
    Task CompleteAsync(CleanupJob job,CancellationToken cancellationToken);
    Task PartialAsync(CleanupJob job,IReadOnlyList<string> remaining,string error,CancellationToken cancellationToken);
}

public sealed class MediaCleanupStore:IMediaCleanupStore
{
    private readonly string _connectionString;
    public MediaCleanupStore(IConfiguration configuration)=>_connectionString=configuration.GetConnectionString("DefaultConnection")??throw new InvalidOperationException("Missing database connection.");
    public async Task<CleanupJob?> ClaimAsync(string workerId,CancellationToken cancellationToken)
    {
        const string sql="""
            SET XACT_ABORT ON;BEGIN TRANSACTION;
            DECLARE @Id bigint=(SELECT TOP 1 j.mediaCleanupJobId
              FROM dbo.mediaCleanupJobs j WITH(UPDLOCK,READPAST,ROWLOCK)
              WHERE (j.status='CleanupPending' OR (j.status='Processing' AND j.leaseExpiresAt<SYSUTCDATETIME()))
                AND j.notBefore<=SYSUTCDATETIME()
                AND NOT EXISTS(
                  SELECT 1 FROM dbo.bookingRecaps other
                  WHERE other.bookingId=j.bookingId AND other.recapId<>j.recapId
                    AND other.status IN('Queued','Processing','Completed','Uploading','Published'))
              ORDER BY j.notBefore);
            IF @Id IS NOT NULL UPDATE dbo.mediaCleanupJobs SET status='Processing',attemptCount=attemptCount+1,
              leaseOwner=@Worker,leaseExpiresAt=DATEADD(minute,15,SYSUTCDATETIME()),updatedAt=SYSUTCDATETIME() WHERE mediaCleanupJobId=@Id;
            COMMIT;
            SELECT j.mediaCleanupJobId,j.bookingId,j.recapId,j.archiveId,r.sourceMediaVersion,r.outputPrivateStorageReference
            FROM dbo.mediaCleanupJobs j JOIN dbo.bookingRecaps r ON r.recapId=j.recapId
            JOIN dbo.bookingVideoArchives a ON a.archiveId=j.archiveId AND a.recapId=j.recapId AND a.youtubeVideoId=r.youtubeVideoId
            WHERE j.mediaCleanupJobId=@Id AND r.status='Published' AND r.uploadStatus='Published' AND a.verifiedAt IS NOT NULL;
            """;
        await using var connection=new SqlConnection(_connectionString);await using var command=new SqlCommand(sql,connection);command.Parameters.Add("@Worker",SqlDbType.NVarChar,120).Value=workerId;await connection.OpenAsync(cancellationToken);
        long id;int booking;long recap,archive;int version;string? recapRef;
        await using(var reader=await command.ExecuteReaderAsync(cancellationToken)){if(!await reader.ReadAsync(cancellationToken))return null;id=reader.GetInt64(0);booking=reader.GetInt32(1);recap=reader.GetInt64(2);archive=reader.GetInt64(3);version=reader.GetInt32(4);recapRef=reader.IsDBNull(5)?null:reader.GetString(5);}
        const string mediaSql="SELECT mediaId,privateStorageReference FROM dbo.bookingMedia WHERE bookingId=@Booking AND mediaVersion<=@Version AND deletedAt IS NULL;";
        await using var mediaCommand=new SqlCommand(mediaSql,connection);mediaCommand.Parameters.Add("@Booking",SqlDbType.Int).Value=booking;mediaCommand.Parameters.Add("@Version",SqlDbType.Int).Value=version;
        var media=new List<(long,string)>();await using var mediaReader=await mediaCommand.ExecuteReaderAsync(cancellationToken);while(await mediaReader.ReadAsync(cancellationToken))media.Add((mediaReader.GetInt64(0),mediaReader.GetString(1)));
        return new CleanupJob(id,booking,recap,archive,version,recapRef,media);
    }
    public async Task<bool> RequestImmediateAsync(long recapId,int requestedByUserId,CancellationToken cancellationToken)
    {const string sql="UPDATE dbo.mediaCleanupJobs SET requestedByUserId=@User,notBefore=SYSUTCDATETIME(),status='CleanupPending',updatedAt=SYSUTCDATETIME() WHERE recapId=@Recap AND status IN('CleanupPending','Retained');";await using var c=new SqlConnection(_connectionString);await using var cmd=new SqlCommand(sql,c);cmd.Parameters.Add("@User",SqlDbType.Int).Value=requestedByUserId;cmd.Parameters.Add("@Recap",SqlDbType.BigInt).Value=recapId;await c.OpenAsync(cancellationToken);return await cmd.ExecuteNonQueryAsync(cancellationToken)==1;}
    public async Task<object?> GetStatusAsync(long recapId,CancellationToken cancellationToken)
    {const string sql="SELECT j.status,j.notBefore,j.attemptCount,j.lastError,a.cleanupStartedAt,a.cleanupCompletedAt,a.sourceFilesDeletedAt FROM dbo.mediaCleanupJobs j JOIN dbo.bookingVideoArchives a ON a.archiveId=j.archiveId WHERE j.recapId=@Recap;";await using var c=new SqlConnection(_connectionString);await using var cmd=new SqlCommand(sql,c);cmd.Parameters.Add("@Recap",SqlDbType.BigInt).Value=recapId;await c.OpenAsync(cancellationToken);await using var r=await cmd.ExecuteReaderAsync(cancellationToken);if(!await r.ReadAsync(cancellationToken))return null;return new{status=r.GetString(0),notBefore=r.GetDateTime(1),attemptCount=r.GetInt32(2),failureReason=r.IsDBNull(3)?null:r.GetString(3),cleanupStartedAt=r.IsDBNull(4)?(DateTime?)null:r.GetDateTime(4),cleanupCompletedAt=r.IsDBNull(5)?(DateTime?)null:r.GetDateTime(5),sourceFilesDeletedAt=r.IsDBNull(6)?(DateTime?)null:r.GetDateTime(6)};}
    public async Task CompleteAsync(CleanupJob job,CancellationToken cancellationToken)
    {const string sql="""
        SET XACT_ABORT ON;BEGIN TRANSACTION;
        UPDATE dbo.bookingMedia SET deletedAt=COALESCE(deletedAt,SYSUTCDATETIME()),deletionReason='Archived after verified YouTube publication',inclusionStatus='Archived',updatedAt=SYSUTCDATETIME()
          WHERE bookingId=@Booking AND mediaVersion<=@Version;
        UPDATE dbo.bookingRecaps SET status='Archived',deletionStatus='Completed',outputPrivateStorageReference=NULL WHERE recapId=@Recap AND status='Published';
        UPDATE dbo.bookingVideoArchives SET cleanupStatus='Completed',cleanupStartedAt=COALESCE(cleanupStartedAt,SYSUTCDATETIME()),cleanupCompletedAt=SYSUTCDATETIME(),sourceFilesDeletedAt=SYSUTCDATETIME() WHERE archiveId=@Archive;
        UPDATE dbo.mediaCleanupJobs SET status='Completed',remainingFilesJson=NULL,lastError=NULL,leaseOwner=NULL,leaseExpiresAt=NULL,updatedAt=SYSUTCDATETIME() WHERE mediaCleanupJobId=@Id;
        COMMIT;
        """;await Exec(sql,job,cancellationToken);}
    public async Task PartialAsync(CleanupJob job,IReadOnlyList<string> remaining,string error,CancellationToken cancellationToken)
    {const string sql="""
        UPDATE dbo.mediaCleanupJobs SET status='CleanupPending',remainingFilesJson=@Files,lastError=@Error,notBefore=DATEADD(minute,15,SYSUTCDATETIME()),leaseOwner=NULL,leaseExpiresAt=NULL,updatedAt=SYSUTCDATETIME() WHERE mediaCleanupJobId=@Id;
        UPDATE dbo.bookingVideoArchives SET cleanupStatus='PartialFailure',cleanupStartedAt=COALESCE(cleanupStartedAt,SYSUTCDATETIME()) WHERE archiveId=@Archive;
        UPDATE dbo.bookingRecaps SET deletionStatus='CleanupPending' WHERE recapId=@Recap;
        """;await Exec(sql,job,cancellationToken,JsonSerializer.Serialize(remaining),error);}
    private async Task Exec(string sql,CleanupJob job,CancellationToken token,string? files=null,string? error=null)
    {await using var c=new SqlConnection(_connectionString);await using var cmd=new SqlCommand(sql,c);cmd.Parameters.Add("@Id",SqlDbType.BigInt).Value=job.JobId;cmd.Parameters.Add("@Booking",SqlDbType.Int).Value=job.BookingId;cmd.Parameters.Add("@Version",SqlDbType.Int).Value=job.SourceMediaVersion;cmd.Parameters.Add("@Recap",SqlDbType.BigInt).Value=job.RecapId;cmd.Parameters.Add("@Archive",SqlDbType.BigInt).Value=job.ArchiveId;if(sql.Contains("@Files"))cmd.Parameters.Add("@Files",SqlDbType.NVarChar,-1).Value=files!;if(sql.Contains("@Error"))cmd.Parameters.Add("@Error",SqlDbType.NVarChar,1000).Value=error!;await c.OpenAsync(token);await cmd.ExecuteNonQueryAsync(token);}
}

public sealed class MediaCleanupWorker:BackgroundService
{
    private readonly IServiceScopeFactory _scopes;private readonly ILogger<MediaCleanupWorker> _logger;
    private readonly string _workerId=$"{Environment.MachineName}-{Guid.NewGuid():N}";
    public MediaCleanupWorker(IServiceScopeFactory scopes,ILogger<MediaCleanupWorker> logger){_scopes=scopes;_logger=logger;}
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        while(!token.IsCancellationRequested)try
        {using var scope=_scopes.CreateScope();var store=scope.ServiceProvider.GetRequiredService<IMediaCleanupStore>();var job=await store.ClaimAsync(_workerId,token);if(job is null){await Task.Delay(TimeSpan.FromSeconds(20),token);continue;}var storage=scope.ServiceProvider.GetRequiredService<ISharedBookingStorage>();var remaining=new List<string>();foreach(var item in job.Media){if(!await storage.DeleteAsync(item.Reference,token)&&storage.TryGetPhoto(item.Reference,job.BookingId,out _))remaining.Add(item.Reference);}if(!string.IsNullOrWhiteSpace(job.RecapReference)&&!await storage.DeleteAsync(job.RecapReference,token)&&storage.TryGetRecap(job.RecapReference,job.BookingId,out _))remaining.Add(job.RecapReference);if(remaining.Count==0)await store.CompleteAsync(job,token);else await store.PartialAsync(job,remaining,"Some scoped source files could not be deleted; cleanup will retry.",token);}
        catch(OperationCanceledException)when(token.IsCancellationRequested){}catch(Exception e){_logger.LogError(e,"Media cleanup worker loop failed");await Task.Delay(TimeSpan.FromSeconds(20),token);}
    }
}
