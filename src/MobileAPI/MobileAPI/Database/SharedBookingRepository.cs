using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using MobileApi.Models;

namespace MobileApi.Database;

public sealed record BookingMediaUsage(int BookingCount, int UploaderCount, long BookingBytes, int UploaderVideoCount = 0);

public interface ISharedBookingRepository
{
    Task<BookingMediaUsage> GetMediaUsageAsync(int bookingId, int uploaderUserId, CancellationToken cancellationToken);
    Task<long> AddMediaAsync(BookingMediaDto media, CancellationToken cancellationToken);
    Task<List<BookingMediaDto>> ListMediaAsync(int bookingId, CancellationToken cancellationToken);
    Task<BookingMediaDto?> GetMediaAsync(long mediaId, CancellationToken cancellationToken);
    Task<bool> UpdateMediaAsync(long mediaId, BookingMediaUpdateRequest request, CancellationToken cancellationToken);
    Task<bool> ModerateMediaAsync(long mediaId, string inclusionStatus, string reason, CancellationToken cancellationToken);
    Task<bool> SoftDeleteMediaAsync(long mediaId, string reason, CancellationToken cancellationToken);
    Task<long> AddRouteAsync(int bookingId, MobileRoleContext caller, SubmitBookingRouteRequest request, CancellationToken cancellationToken);
    Task<List<BookingRouteDto>> ListRoutesAsync(int bookingId, bool includePrecisePoints, CancellationToken cancellationToken);
    Task<BookingRouteDto?> GetRouteAsync(long routeId, bool includePrecisePoints, CancellationToken cancellationToken);
    Task<bool> SelectRouteAsync(int bookingId, long routeId, int selectedByUserId, CancellationToken cancellationToken);
    Task<BookingRecapDto> QueueRecapAsync(int bookingId, int requestedByUserId, string title, int maxJobs, CancellationToken cancellationToken);
    Task<List<BookingRecapDto>> ListRecapsAsync(int bookingId, CancellationToken cancellationToken);
    Task<BookingRecapDto?> GetRecapAsync(long recapId, CancellationToken cancellationToken);
    Task<bool> RetryRecapAsync(long recapId, CancellationToken cancellationToken);
}

public sealed class SharedBookingRepository : ISharedBookingRepository
{
    private readonly string _connectionString;
    public SharedBookingRepository(IConfiguration configuration) =>
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' was not found.");

    public async Task<BookingMediaUsage> GetMediaUsageAsync(int bookingId, int uploaderUserId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT COUNT(*),
                   SUM(CASE WHEN uploaderUserId = @UploaderUserId THEN 1 ELSE 0 END),
                   ISNULL(SUM(fileSizeBytes), 0),
                   SUM(CASE WHEN uploaderUserId = @UploaderUserId AND sourceType = 'Video' THEN 1 ELSE 0 END)
            FROM dbo.bookingMedia
            WHERE bookingId = @BookingId AND deletedAt IS NULL;
            """;
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@BookingId", SqlDbType.Int).Value = bookingId;
        command.Parameters.Add("@UploaderUserId", SqlDbType.Int).Value = uploaderUserId;
        await connection.OpenAsync(cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new BookingMediaUsage(reader.GetInt32(0), reader.IsDBNull(1) ? 0 : reader.GetInt32(1), reader.GetInt64(2),
            reader.IsDBNull(3) ? 0 : reader.GetInt32(3));
    }

    public async Task<long> AddMediaAsync(BookingMediaDto media, CancellationToken cancellationToken)
    {
        const string sql = """
            DECLARE @Version int = ISNULL((SELECT MAX(mediaVersion) FROM dbo.bookingMedia WITH (UPDLOCK, HOLDLOCK) WHERE bookingId = @BookingId), 0) + 1;
            INSERT dbo.bookingMedia
              (bookingId, uploaderUserId, uploaderUserRoleId, uploaderRole, originalFileName,
               privateStorageReference, contentType, fileSizeBytes, width, height,
               originalCaptureTimestamp, latitude, longitude, orientation, caption,
               matchMethod, matchConfidence, matchedRoutePointIndex, matchedPinId, mediaVersion,
               sourceType, durationSeconds)
            OUTPUT inserted.mediaId
            VALUES
              (@BookingId, @UploaderUserId, @UploaderUserRoleId, @UploaderRole, @OriginalFileName,
               @StorageReference, @ContentType, @FileSizeBytes, @Width, @Height,
               @CapturedAt, @Latitude, @Longitude, @Orientation, @Caption,
               @MatchMethod, @MatchConfidence, @MatchedRoutePointIndex, @MatchedPinId, @Version,
               @SourceType, @DurationSeconds);
            """;
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand(sql, connection);
        AddMediaParameters(command, media);
        await connection.OpenAsync(cancellationToken);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<List<BookingMediaDto>> ListMediaAsync(int bookingId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT mediaId, bookingId, uploaderUserId, uploaderRole, originalFileName,
                   privateStorageReference, contentType, fileSizeBytes, width, height,
                   originalCaptureTimestamp, adjustedCaptureTimestamp, latitude, longitude,
                   orientation, caption, matchMethod, matchConfidence, matchedRoutePointIndex,
                   matchedPinId, inclusionStatus, reviewStatus, mediaVersion, createdAt, updatedAt,
                   sourceType, durationSeconds
            FROM dbo.bookingMedia
            WHERE bookingId = @BookingId AND deletedAt IS NULL
            ORDER BY COALESCE(adjustedCaptureTimestamp, originalCaptureTimestamp, createdAt), mediaId;
            """;
        var result = new List<BookingMediaDto>();
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@BookingId", SqlDbType.Int).Value = bookingId;
        await connection.OpenAsync(cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(ReadMedia(reader));
        return result;
    }

    public async Task<BookingMediaDto?> GetMediaAsync(long mediaId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT mediaId, bookingId, uploaderUserId, uploaderRole, originalFileName,
                   privateStorageReference, contentType, fileSizeBytes, width, height,
                   originalCaptureTimestamp, adjustedCaptureTimestamp, latitude, longitude,
                   orientation, caption, matchMethod, matchConfidence, matchedRoutePointIndex,
                   matchedPinId, inclusionStatus, reviewStatus, mediaVersion, createdAt, updatedAt,
                   sourceType, durationSeconds
            FROM dbo.bookingMedia WHERE mediaId = @MediaId AND deletedAt IS NULL;
            """;
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@MediaId", SqlDbType.BigInt).Value = mediaId;
        await connection.OpenAsync(cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadMedia(reader) : null;
    }

    public async Task<bool> UpdateMediaAsync(long mediaId, BookingMediaUpdateRequest request, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE dbo.bookingMedia
            SET caption = @Caption,
                adjustedCaptureTimestamp = @AdjustedTimestamp,
                matchedRoutePointIndex = @RoutePoint,
                matchedPinId = @PinId,
                matchMethod = @MatchMethod,
                matchConfidence = CASE WHEN @MatchMethod = 'Manual' THEN 'High' ELSE matchConfidence END,
                mediaVersion = (SELECT ISNULL(MAX(other.mediaVersion), 0) + 1
                    FROM dbo.bookingMedia other WITH (UPDLOCK, HOLDLOCK)
                    WHERE other.bookingId = dbo.bookingMedia.bookingId),
                updatedAt = SYSUTCDATETIME()
            WHERE mediaId = @MediaId AND deletedAt IS NULL;
            """;
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@MediaId", SqlDbType.BigInt).Value = mediaId;
        command.Parameters.Add("@Caption", SqlDbType.NVarChar, 500).Value = Db(request.Caption?.Trim());
        command.Parameters.Add("@AdjustedTimestamp", SqlDbType.DateTime2).Value = Db(request.AdjustedCaptureTimestamp?.ToUniversalTime());
        command.Parameters.Add("@RoutePoint", SqlDbType.Int).Value = Db(request.MatchedRoutePointIndex);
        command.Parameters.Add("@PinId", SqlDbType.NVarChar, 120).Value = Db(request.MatchedPinId?.Trim());
        command.Parameters.Add("@MatchMethod", SqlDbType.NVarChar, 30).Value = string.IsNullOrWhiteSpace(request.MatchMethod) ? "Manual" : request.MatchMethod.Trim();
        await connection.OpenAsync(cancellationToken);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<bool> ModerateMediaAsync(long mediaId, string inclusionStatus, string reason, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE dbo.bookingMedia
            SET inclusionStatus = @Status, reviewStatus = 'Reviewed',
                deletionReason = CASE WHEN @Status = 'Excluded' THEN @Reason ELSE deletionReason END,
                mediaVersion = (SELECT ISNULL(MAX(other.mediaVersion), 0) + 1
                    FROM dbo.bookingMedia other WITH (UPDLOCK, HOLDLOCK)
                    WHERE other.bookingId = dbo.bookingMedia.bookingId),
                updatedAt = SYSUTCDATETIME()
            WHERE mediaId = @MediaId AND deletedAt IS NULL;
            """;
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@MediaId", SqlDbType.BigInt).Value = mediaId;
        command.Parameters.Add("@Status", SqlDbType.NVarChar, 30).Value = inclusionStatus;
        command.Parameters.Add("@Reason", SqlDbType.NVarChar, 300).Value = Db(reason);
        await connection.OpenAsync(cancellationToken);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<bool> SoftDeleteMediaAsync(long mediaId, string reason, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE dbo.bookingMedia SET deletedAt = SYSUTCDATETIME(), deletionReason = @Reason,
                inclusionStatus = 'Deleted', updatedAt = SYSUTCDATETIME()
            WHERE mediaId = @MediaId AND deletedAt IS NULL;
            """;
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@MediaId", SqlDbType.BigInt).Value = mediaId;
        command.Parameters.Add("@Reason", SqlDbType.NVarChar, 300).Value = reason;
        await connection.OpenAsync(cancellationToken);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<long> AddRouteAsync(int bookingId, MobileRoleContext caller, SubmitBookingRouteRequest request, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT dbo.bookingRoutes
              (bookingId, ownerUserId, ownerUserRoleId, sourceHuntSessionId, routePointsJson,
               pinsJson, pointCount, distanceMeters, durationSeconds, routeVersion)
            OUTPUT inserted.routeId
            VALUES (@BookingId, @UserId, @UserRoleId, @SessionId, @Points, @Pins,
                    @PointCount, @Distance, @Duration, @Version);
            """;
        var points = JsonSerializer.Serialize(request.Points, JsonDefaults.Options);
        var pins = JsonSerializer.Serialize(request.Pins, JsonDefaults.Options);
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@BookingId", SqlDbType.Int).Value = bookingId;
        command.Parameters.Add("@UserId", SqlDbType.Int).Value = caller.UserId;
        command.Parameters.Add("@UserRoleId", SqlDbType.Int).Value = caller.UserRoleId;
        command.Parameters.Add("@SessionId", SqlDbType.NVarChar, 120).Value = request.SourceHuntSessionId.Trim();
        command.Parameters.Add("@Points", SqlDbType.NVarChar, -1).Value = points;
        command.Parameters.Add("@Pins", SqlDbType.NVarChar, -1).Value = pins;
        command.Parameters.Add("@PointCount", SqlDbType.Int).Value = request.Points.Count;
        command.Parameters.Add("@Distance", SqlDbType.Decimal).Value = Convert.ToDecimal(request.DistanceMeters);
        command.Parameters.Add("@Duration", SqlDbType.Int).Value = request.DurationSeconds;
        command.Parameters.Add("@Version", SqlDbType.Int).Value = request.RouteVersion;
        await connection.OpenAsync(cancellationToken);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    public Task<List<BookingRouteDto>> ListRoutesAsync(int bookingId, bool includePrecisePoints, CancellationToken cancellationToken) =>
        ReadRoutesAsync("r.bookingId = @Id", bookingId, includePrecisePoints, cancellationToken);

    public async Task<BookingRouteDto?> GetRouteAsync(long routeId, bool includePrecisePoints, CancellationToken cancellationToken)
    {
        var rows = await ReadRoutesAsync("r.routeId = @Id", routeId, includePrecisePoints, cancellationToken);
        return rows.SingleOrDefault();
    }

    private async Task<List<BookingRouteDto>> ReadRoutesAsync(string predicate, object id, bool includePrecisePoints, CancellationToken cancellationToken)
    {
        var sql = $"""
            SELECT r.routeId, r.bookingId, r.ownerUserId, r.sourceHuntSessionId,
                   r.routePointsJson, r.pinsJson, r.pointCount, r.distanceMeters,
                   r.durationSeconds, r.routeVersion, r.status, r.submittedAt,
                   CASE WHEN s.routeId IS NULL THEN CAST(0 AS bit) ELSE CAST(1 AS bit) END
            FROM dbo.bookingRoutes r
            LEFT JOIN dbo.bookingRouteSelections s ON s.routeId = r.routeId AND s.supersededAt IS NULL
            WHERE {predicate}
            ORDER BY r.submittedAt DESC;
            """;
        var result = new List<BookingRouteDto>();
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Id", id);
        await connection.OpenAsync(cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new BookingRouteDto
            {
                RouteId = reader.GetInt64(0), BookingId = reader.GetInt32(1), OwnerUserId = reader.GetInt32(2),
                SourceHuntSessionId = reader.GetString(3),
                Points = includePrecisePoints ? BookingRouteDto.ParsePoints(reader.GetString(4)) : [],
                Pins = includePrecisePoints ? BookingRouteDto.ParsePins(reader.IsDBNull(5) ? null : reader.GetString(5)) : [],
                PointCount = reader.GetInt32(6), DistanceMeters = reader.GetDecimal(7), DurationSeconds = reader.GetInt32(8),
                RouteVersion = reader.GetInt32(9), Status = reader.GetString(10), SubmittedAt = reader.GetDateTime(11),
                SelectedForRecap = reader.GetBoolean(12)
            });
        }
        return result;
    }

    public async Task<bool> SelectRouteAsync(int bookingId, long routeId, int selectedByUserId, CancellationToken cancellationToken)
    {
        const string sql = """
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;
            IF NOT EXISTS (SELECT 1 FROM dbo.bookingRoutes WHERE routeId = @RouteId AND bookingId = @BookingId AND status <> 'Rejected')
            BEGIN ROLLBACK; SELECT CAST(0 AS bit); RETURN; END;
            UPDATE dbo.bookingRouteSelections SET supersededAt = SYSUTCDATETIME()
             WHERE bookingId = @BookingId AND supersededAt IS NULL;
            INSERT dbo.bookingRouteSelections(bookingId, routeId, selectedByUserId)
             VALUES(@BookingId, @RouteId, @UserId);
            UPDATE dbo.bookingRoutes SET status = CASE WHEN routeId = @RouteId THEN 'Selected' ELSE status END,
                updatedAt = SYSUTCDATETIME() WHERE bookingId = @BookingId;
            COMMIT; SELECT CAST(1 AS bit);
            """;
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@BookingId", SqlDbType.Int).Value = bookingId;
        command.Parameters.Add("@RouteId", SqlDbType.BigInt).Value = routeId;
        command.Parameters.Add("@UserId", SqlDbType.Int).Value = selectedByUserId;
        await connection.OpenAsync(cancellationToken);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<BookingRecapDto> QueueRecapAsync(int bookingId, int requestedByUserId, string title, int maxJobs, CancellationToken cancellationToken)
    {
        const string sql = """
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;
            DECLARE @RouteId bigint = (SELECT routeId FROM dbo.bookingRouteSelections WITH (UPDLOCK, HOLDLOCK) WHERE bookingId = @BookingId AND supersededAt IS NULL);
            IF @RouteId IS NULL THROW 51001, 'A selected route is required.', 1;
            IF (SELECT COUNT(*) FROM dbo.bookingRecaps WITH (UPDLOCK, HOLDLOCK) WHERE bookingId = @BookingId) >= @MaxJobs
                THROW 51002, 'The recap job limit has been reached.', 1;
            IF EXISTS (SELECT 1 FROM dbo.bookingRecaps WHERE bookingId = @BookingId AND status IN ('Queued','Processing','Completed','Uploading','Published'))
                THROW 51003, 'An active recap already exists.', 1;
            DECLARE @Version int = ISNULL((SELECT MAX(version) FROM dbo.bookingRecaps WHERE bookingId = @BookingId), 0) + 1;
            DECLARE @MediaVersion int = ISNULL((SELECT MAX(mediaVersion) FROM dbo.bookingMedia WHERE bookingId = @BookingId AND deletedAt IS NULL AND inclusionStatus = 'Included'), 0);
            IF @MediaVersion = 0 THROW 51004, 'At least one included photo is required.', 1;
            INSERT dbo.bookingRecaps(bookingId, selectedRouteId, requestedByUserId, status, progress, sourceMediaVersion, title, version)
            VALUES(@BookingId, @RouteId, @UserId, 'Queued', 0, @MediaVersion, @Title, @Version);
            DECLARE @RecapId bigint = SCOPE_IDENTITY();
            INSERT dbo.recapProcessingJobs(recapId, idempotencyKey) VALUES(@RecapId, NEWID());
            COMMIT;
            SELECT @RecapId;
            """;
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@BookingId", SqlDbType.Int).Value = bookingId;
        command.Parameters.Add("@UserId", SqlDbType.Int).Value = requestedByUserId;
        command.Parameters.Add("@Title", SqlDbType.NVarChar, 180).Value = title;
        command.Parameters.Add("@MaxJobs", SqlDbType.Int).Value = maxJobs;
        await connection.OpenAsync(cancellationToken);
        var recapId = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
        return (await GetRecapAsync(recapId, cancellationToken))!;
    }

    public async Task<List<BookingRecapDto>> ListRecapsAsync(int bookingId, CancellationToken cancellationToken)
    {
        var result = new List<BookingRecapDto>();
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand(RecapSelect + " WHERE r.bookingId = @Id ORDER BY r.createdAt DESC;", connection);
        command.Parameters.Add("@Id", SqlDbType.Int).Value = bookingId;
        await connection.OpenAsync(cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(ReadRecap(reader));
        return result;
    }

    public async Task<BookingRecapDto?> GetRecapAsync(long recapId, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand(RecapSelect + " WHERE r.recapId = @Id;", connection);
        command.Parameters.Add("@Id", SqlDbType.BigInt).Value = recapId;
        await connection.OpenAsync(cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadRecap(reader) : null;
    }

    public async Task<bool> RetryRecapAsync(long recapId, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE dbo.bookingRecaps SET status = 'Queued', progress = 0, failureReason = NULL, startedAt = NULL, completedAt = NULL
             WHERE recapId = @Id AND status = 'Failed';
            UPDATE dbo.recapProcessingJobs SET status = 'Queued', nextAttemptAt = SYSUTCDATETIME(), lastError = NULL,
                leaseOwner = NULL, leaseExpiresAt = NULL, updatedAt = SYSUTCDATETIME()
             WHERE recapId = @Id AND @@ROWCOUNT = 1;
            SELECT @@ROWCOUNT;
            """;
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Id", SqlDbType.BigInt).Value = recapId;
        await connection.OpenAsync(cancellationToken);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    private const string RecapSelect = """
        SELECT r.recapId, r.bookingId, r.selectedRouteId, r.status, r.progress, r.sourceMediaVersion,
               r.outputPrivateStorageReference, r.outputFileSize, r.durationSeconds, r.title,
               r.failureReason, r.version, r.uploadStatus, r.deletionStatus, r.youtubeUrl,
               r.youtubePrivacyStatus, r.youtubeProcessingStatus, r.createdAt, r.completedAt,
               CAST(b.bookStartDate AS datetime2), CAST(b.bookEndDate AS datetime2),
               ISNULL((SELECT TOP 1 job.uploadProgress FROM dbo.youtubePublishJobs job
                   WHERE job.recapId=r.recapId ORDER BY job.createdAt DESC),0),
               (SELECT TOP 1 job.verifiedAt FROM dbo.youtubePublishJobs job
                   WHERE job.recapId=r.recapId ORDER BY job.createdAt DESC)
        FROM dbo.bookingRecaps r
        JOIN dbo.bookings b ON b.bookingId = r.bookingId
        """;

    private static BookingRecapDto ReadRecap(SqlDataReader reader) => new()
    {
        RecapId = reader.GetInt64(0), BookingId = reader.GetInt32(1), SelectedRouteId = reader.GetInt64(2),
        Status = reader.GetString(3), Progress = reader.GetInt32(4), SourceMediaVersion = reader.GetInt32(5),
        OutputPrivateStorageReference = reader.IsDBNull(6) ? null : reader.GetString(6),
        OutputFileSize = reader.IsDBNull(7) ? null : reader.GetInt64(7), DurationSeconds = reader.IsDBNull(8) ? null : reader.GetInt32(8),
        Title = reader.GetString(9), FailureReason = reader.IsDBNull(10) ? null : reader.GetString(10), Version = reader.GetInt32(11),
        UploadStatus = reader.GetString(12), DeletionStatus = reader.GetString(13), YoutubeUrl = reader.IsDBNull(14) ? null : reader.GetString(14),
        YoutubePrivacyStatus = reader.IsDBNull(15) ? null : reader.GetString(15), YoutubeProcessingStatus = reader.IsDBNull(16) ? null : reader.GetString(16),
        CreatedAt = reader.GetDateTime(17), CompletedAt = reader.IsDBNull(18) ? null : reader.GetDateTime(18),
        BookingStartDate = reader.GetDateTime(19), BookingEndDate = reader.GetDateTime(20), UploadProgress = reader.GetInt32(21),
        YoutubeVerifiedAt = reader.IsDBNull(22) ? null : reader.GetDateTime(22)
    };

    private static BookingMediaDto ReadMedia(SqlDataReader reader) => new()
    {
        MediaId = reader.GetInt64(0), BookingId = reader.GetInt32(1), UploaderUserId = reader.GetInt32(2), UploaderRole = reader.GetString(3),
        OriginalFileName = reader.GetString(4), PrivateStorageReference = reader.GetString(5), ContentType = reader.GetString(6), FileSizeBytes = reader.GetInt64(7),
        Width = reader.GetInt32(8), Height = reader.GetInt32(9), OriginalCaptureTimestamp = reader.IsDBNull(10) ? null : reader.GetDateTime(10),
        AdjustedCaptureTimestamp = reader.IsDBNull(11) ? null : reader.GetDateTime(11), Latitude = reader.IsDBNull(12) ? null : reader.GetDecimal(12),
        Longitude = reader.IsDBNull(13) ? null : reader.GetDecimal(13), Orientation = reader.IsDBNull(14) ? null : reader.GetInt32(14),
        Caption = reader.IsDBNull(15) ? null : reader.GetString(15), MatchMethod = reader.GetString(16), MatchConfidence = reader.GetString(17),
        MatchedRoutePointIndex = reader.IsDBNull(18) ? null : reader.GetInt32(18), MatchedPinId = reader.IsDBNull(19) ? null : reader.GetString(19),
        InclusionStatus = reader.GetString(20), ReviewStatus = reader.GetString(21), MediaVersion = reader.GetInt32(22), CreatedAt = reader.GetDateTime(23), UpdatedAt = reader.GetDateTime(24),
        DownloadPath = $"/api/booking-media/{reader.GetInt64(0)}/content",
        SourceType = reader.GetString(25), DurationSeconds = reader.IsDBNull(26) ? null : reader.GetDecimal(26)
    };

    private static void AddMediaParameters(SqlCommand command, BookingMediaDto media)
    {
        command.Parameters.Add("@BookingId", SqlDbType.Int).Value = media.BookingId;
        command.Parameters.Add("@UploaderUserId", SqlDbType.Int).Value = media.UploaderUserId;
        command.Parameters.Add("@UploaderUserRoleId", SqlDbType.Int).Value = media.UploaderUserRoleId;
        command.Parameters.Add("@UploaderRole", SqlDbType.NVarChar, 20).Value = media.UploaderRole;
        command.Parameters.Add("@OriginalFileName", SqlDbType.NVarChar, 255).Value = media.OriginalFileName;
        command.Parameters.Add("@StorageReference", SqlDbType.NVarChar, 700).Value = media.PrivateStorageReference;
        command.Parameters.Add("@ContentType", SqlDbType.NVarChar, 80).Value = media.ContentType;
        command.Parameters.Add("@FileSizeBytes", SqlDbType.BigInt).Value = media.FileSizeBytes;
        command.Parameters.Add("@Width", SqlDbType.Int).Value = media.Width;
        command.Parameters.Add("@Height", SqlDbType.Int).Value = media.Height;
        command.Parameters.Add("@CapturedAt", SqlDbType.DateTime2).Value = Db(media.OriginalCaptureTimestamp?.ToUniversalTime());
        command.Parameters.Add("@Latitude", SqlDbType.Decimal).Value = Db(media.Latitude);
        command.Parameters.Add("@Longitude", SqlDbType.Decimal).Value = Db(media.Longitude);
        command.Parameters.Add("@Orientation", SqlDbType.Int).Value = Db(media.Orientation);
        command.Parameters.Add("@Caption", SqlDbType.NVarChar, 500).Value = Db(media.Caption);
        command.Parameters.Add("@MatchMethod", SqlDbType.NVarChar, 30).Value = media.MatchMethod;
        command.Parameters.Add("@MatchConfidence", SqlDbType.NVarChar, 20).Value = media.MatchConfidence;
        command.Parameters.Add("@MatchedRoutePointIndex", SqlDbType.Int).Value = Db(media.MatchedRoutePointIndex);
        command.Parameters.Add("@MatchedPinId", SqlDbType.NVarChar, 120).Value = Db(media.MatchedPinId);
        command.Parameters.Add("@SourceType", SqlDbType.NVarChar, 30).Value = media.SourceType;
        var duration = command.Parameters.Add("@DurationSeconds", SqlDbType.Decimal);
        duration.Precision = 9; duration.Scale = 3;
        duration.Value = Db(media.DurationSeconds);
    }

    private static object Db(object? value) => value ?? DBNull.Value;
}
