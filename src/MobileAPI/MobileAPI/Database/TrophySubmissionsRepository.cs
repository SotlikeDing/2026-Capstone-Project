using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using MobileApi.Models;
using MobileApi.Models.RowlandWard;

namespace MobileApi.Database;

public enum TrophyReviewOutcome { Reviewed, NotFound, Forbidden, AlreadyReviewed, DuplicateAction }

public interface ITrophySubmissionsRepository
{
    Task<long> CreateAsync(MobileRoleContext caller, SubmitTrophyRequest request, CancellationToken cancellationToken);
    Task<List<TrophySubmissionDto>> ListForHunterAsync(int hunterUserId, CancellationToken cancellationToken);
    Task<List<TrophySubmissionDto>> ListForPhAsync(int phUserRoleId, CancellationToken cancellationToken);
    Task<TrophySubmissionRecord?> GetAsync(long submissionId, CancellationToken cancellationToken);
    Task<bool> IsHunterSubmissionAsync(long submissionId, int hunterUserId, CancellationToken cancellationToken);
    Task<TrophyReviewOutcome> ReviewAsync(long submissionId, MobileRoleContext reviewer, TrophyReviewRequest request,
        RowlandWardEvaluationSnapshot? evaluation, CancellationToken cancellationToken);
    Task<bool> ResubmitAsync(long submissionId, int hunterUserId, TrophyResubmitRequest request, CancellationToken cancellationToken);
}

public sealed class TrophySubmissionsRepository : ITrophySubmissionsRepository
{
    private readonly string _connectionString;
    public TrophySubmissionsRepository(IConfiguration configuration) => _connectionString = configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Connection string 'DefaultConnection' was not found.");

    public async Task<long> CreateAsync(MobileRoleContext caller, SubmitTrophyRequest request, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT dbo.trophySubmissions
              (bookingId, hunterUserId, hunterUserRoleId, species, trophyPhotoStorageReference,
               antlerSizeCm, carcassWeightKg, harvestDate, locationText, shotDistanceMeters,
               firearmType, notes, supportingMediaIdsJson, rowlandWardRequestJson, status)
            OUTPUT inserted.submissionId
            VALUES (@BookingId, @UserId, @UserRoleId, @Species, @Photo, @Antler, @Weight,
                    @HarvestDate, @Location, @ShotDistance, @Firearm, @Notes, @Media, @Rowland, 'Pending');
            """;
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@BookingId", SqlDbType.Int).Value = request.BookingId;
        command.Parameters.Add("@UserId", SqlDbType.Int).Value = caller.UserId;
        command.Parameters.Add("@UserRoleId", SqlDbType.Int).Value = caller.UserRoleId;
        command.Parameters.Add("@Species", SqlDbType.NVarChar, 120).Value = request.Species.Trim();
        command.Parameters.Add("@Photo", SqlDbType.NVarChar, 700).Value = request.TrophyPhotoPath.Trim();
        command.Parameters.Add("@Antler", SqlDbType.Decimal).Value = Db(request.AntlerSizeCm);
        command.Parameters.Add("@Weight", SqlDbType.Decimal).Value = Db(request.CarcassWeightKg);
        command.Parameters.Add("@HarvestDate", SqlDbType.Date).Value = request.HarvestDate.Date;
        command.Parameters.Add("@Location", SqlDbType.NVarChar, 200).Value = request.LocationText.Trim();
        command.Parameters.Add("@ShotDistance", SqlDbType.Int).Value = Db(request.ShotDistanceMeters);
        command.Parameters.Add("@Firearm", SqlDbType.NVarChar, 120).Value = request.FirearmType.Trim();
        command.Parameters.Add("@Notes", SqlDbType.NVarChar, -1).Value = Db(request.Notes?.Trim());
        command.Parameters.Add("@Media", SqlDbType.NVarChar, -1).Value = request.SupportingMediaIds.Count == 0 ? DBNull.Value : JsonSerializer.Serialize(request.SupportingMediaIds, JsonDefaults.Options);
        command.Parameters.Add("@Rowland", SqlDbType.NVarChar, -1).Value = request.RowlandWard is null ? DBNull.Value : JsonSerializer.Serialize(request.RowlandWard, JsonDefaults.Options);
        await connection.OpenAsync(cancellationToken);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    public Task<List<TrophySubmissionDto>> ListForHunterAsync(int hunterUserId, CancellationToken cancellationToken) =>
        ListAsync("s.hunterUserId = @Id", hunterUserId, cancellationToken);

    public Task<List<TrophySubmissionDto>> ListForPhAsync(int phUserRoleId, CancellationToken cancellationToken) =>
        ListAsync("EXISTS (SELECT 1 FROM dbo.ph x JOIN dbo.bookings bx ON bx.bookPhId = x.phId WHERE bx.bookingId = s.bookingId AND x.phUserRoleId = @Id)", phUserRoleId, cancellationToken);

    private async Task<List<TrophySubmissionDto>> ListAsync(string predicate, object id, CancellationToken cancellationToken)
    {
        var sql = SelectSql + $" WHERE {predicate} ORDER BY s.createdAt DESC;";
        var result = new List<TrophySubmissionDto>();
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Id", id);
        await connection.OpenAsync(cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(Read(reader));
        return result;
    }

    public async Task<TrophySubmissionRecord?> GetAsync(long submissionId, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand(SelectSql + " WHERE s.submissionId = @Id;", connection);
        command.Parameters.Add("@Id", SqlDbType.BigInt).Value = submissionId;
        await connection.OpenAsync(cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var dto = Read(reader);
        var json = reader.IsDBNull(19) ? null : reader.GetString(19);
        return new TrophySubmissionRecord
        {
            Submission = dto,
            RowlandWard = string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<RowlandWardEvaluationRequest>(json, JsonDefaults.Options)
        };
    }

    public async Task<bool> IsHunterSubmissionAsync(long submissionId, int hunterUserId, CancellationToken cancellationToken)
    {
        const string sql = "SELECT CASE WHEN EXISTS(SELECT 1 FROM dbo.trophySubmissions WHERE submissionId=@Id AND hunterUserId=@UserId) THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END;";
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Id", SqlDbType.BigInt).Value = submissionId;
        command.Parameters.Add("@UserId", SqlDbType.Int).Value = hunterUserId;
        await connection.OpenAsync(cancellationToken);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<TrophyReviewOutcome> ReviewAsync(long submissionId, MobileRoleContext reviewer, TrophyReviewRequest request,
        RowlandWardEvaluationSnapshot? evaluation, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            const string select = """
                SELECT s.status, s.hunterUserId, s.species, s.trophyPhotoStorageReference,
                       s.antlerSizeCm, s.carcassWeightKg, s.shotDistanceMeters, s.firearmType,
                       s.harvestDate, s.locationText, s.notes
                FROM dbo.trophySubmissions s WITH (UPDLOCK, ROWLOCK)
                JOIN dbo.bookings b ON b.bookingId = s.bookingId
                JOIN dbo.ph x ON x.phId = b.bookPhId AND x.phUserRoleId = @RoleId
                WHERE s.submissionId = @Id;
                """;
            await using var selectCommand = new SqlCommand(select, connection, transaction);
            selectCommand.Parameters.Add("@RoleId", SqlDbType.Int).Value = reviewer.UserRoleId;
            selectCommand.Parameters.Add("@Id", SqlDbType.BigInt).Value = submissionId;
            string status; int hunterId; TrophySaveRequest? trophy = null;
            await using (var reader = await selectCommand.ExecuteReaderAsync(cancellationToken))
            {
                if (!await reader.ReadAsync(cancellationToken)) { await transaction.RollbackAsync(cancellationToken); return TrophyReviewOutcome.Forbidden; }
                status = reader.GetString(0);
                hunterId = reader.GetInt32(1);
                trophy = new TrophySaveRequest
                {
                    Species = reader.GetString(2), PhotoPath = reader.GetString(3),
                    AntlerSizeCm = reader.IsDBNull(4) ? null : reader.GetDecimal(4), CarcassWeightKg = reader.IsDBNull(5) ? null : reader.GetDecimal(5),
                    ShotDistanceMeters = reader.IsDBNull(6) ? null : reader.GetInt32(6), FirearmType = reader.GetString(7),
                    HarvestDate = reader.GetDateTime(8), LocationText = reader.GetString(9), Notes = reader.IsDBNull(10) ? null : reader.GetString(10), IsPublic = false
                };
            }
            if (status is not ("Pending" or "Resubmitted" or "UnderReview")) { await transaction.RollbackAsync(cancellationToken); return TrophyReviewOutcome.AlreadyReviewed; }
            if (request.ActionKey == Guid.Empty) request.ActionKey = Guid.NewGuid();

            int? trophyId = null;
            if (request.Action.Equals("Approve", StringComparison.OrdinalIgnoreCase))
            {
                const string insertTrophy = """
                    INSERT dbo.trophies(tOwnerUserId,tSpecies,tPhotoPath,tAntlerSize,tCarcassWeight,tShotDistanceMeters,
                        tFirearmType,tHarvestDate,tLocationText,tNotes,tIsPublic,tApprovalStatus,tSourceSubmissionId,
                        tRowlandWardClassId,tRowlandWardSpeciesNameAf)
                    OUTPUT inserted.trophyId
                    VALUES(@Owner,@Species,@Photo,@Antler,@Weight,@Distance,@Firearm,@Harvest,@Location,@Notes,0,'Approved',@Submission,@RwClass,@RwName);
                    """;
                await using var insert = new SqlCommand(insertTrophy, connection, transaction);
                insert.Parameters.Add("@Owner", SqlDbType.Int).Value = hunterId;
                insert.Parameters.Add("@Species", SqlDbType.NVarChar, 120).Value = evaluation?.DisplayNameAf ?? trophy.Species;
                insert.Parameters.Add("@Photo", SqlDbType.NVarChar, 500).Value = trophy.PhotoPath;
                insert.Parameters.Add("@Antler", SqlDbType.Decimal).Value = Db(trophy.AntlerSizeCm);
                insert.Parameters.Add("@Weight", SqlDbType.Decimal).Value = Db(trophy.CarcassWeightKg);
                insert.Parameters.Add("@Distance", SqlDbType.Int).Value = Db(trophy.ShotDistanceMeters);
                insert.Parameters.Add("@Firearm", SqlDbType.NVarChar, 120).Value = trophy.FirearmType;
                insert.Parameters.Add("@Harvest", SqlDbType.Date).Value = trophy.HarvestDate.Date;
                insert.Parameters.Add("@Location", SqlDbType.NVarChar, 200).Value = trophy.LocationText;
                insert.Parameters.Add("@Notes", SqlDbType.NVarChar, -1).Value = Db(trophy.Notes);
                insert.Parameters.Add("@Submission", SqlDbType.BigInt).Value = submissionId;
                insert.Parameters.Add("@RwClass", SqlDbType.NVarChar, 160).Value = Db(evaluation?.ClassId);
                insert.Parameters.Add("@RwName", SqlDbType.NVarChar, 160).Value = Db(evaluation?.DisplayNameAf);
                trophyId = Convert.ToInt32(await insert.ExecuteScalarAsync(cancellationToken));
                if (evaluation is not null) await RowlandWardEvaluationSql.InsertEvaluationAsync(connection, transaction, trophyId.Value, evaluation);
            }

            var newStatus = request.Action.ToUpperInvariant() switch
            { "APPROVE" => "Approved", "REJECT" => "Rejected", "REQUESTCORRECTION" => "CorrectionRequested", _ => throw new InvalidDataException("Invalid review action.") };
            const string update = """
                UPDATE dbo.trophySubmissions SET status=@Status, reviewNotes=@Reason, approvedTrophyId=@TrophyId, updatedAt=SYSUTCDATETIME() WHERE submissionId=@Id;
                INSERT dbo.trophyReviewActions(submissionId,reviewerUserId,reviewerUserRoleId,reviewerRole,action,reason,actionKey)
                VALUES(@Id,@Reviewer,@RoleId,'PH',@Action,@Reason,@ActionKey);
                """;
            await using var updateCommand = new SqlCommand(update, connection, transaction);
            updateCommand.Parameters.Add("@Status", SqlDbType.NVarChar, 30).Value = newStatus;
            updateCommand.Parameters.Add("@Reason", SqlDbType.NVarChar, 1000).Value = Db(request.Reason?.Trim());
            updateCommand.Parameters.Add("@TrophyId", SqlDbType.Int).Value = Db(trophyId);
            updateCommand.Parameters.Add("@Id", SqlDbType.BigInt).Value = submissionId;
            updateCommand.Parameters.Add("@Reviewer", SqlDbType.Int).Value = reviewer.UserId;
            updateCommand.Parameters.Add("@RoleId", SqlDbType.Int).Value = reviewer.UserRoleId;
            updateCommand.Parameters.Add("@Action", SqlDbType.NVarChar, 30).Value = request.Action;
            updateCommand.Parameters.Add("@ActionKey", SqlDbType.UniqueIdentifier).Value = request.ActionKey;
            await updateCommand.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return TrophyReviewOutcome.Reviewed;
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627)
        { await transaction.RollbackAsync(cancellationToken); return TrophyReviewOutcome.DuplicateAction; }
        catch { await transaction.RollbackAsync(cancellationToken); throw; }
    }

    public async Task<bool> ResubmitAsync(long submissionId, int hunterUserId, TrophyResubmitRequest request, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE dbo.trophySubmissions SET status='Resubmitted', notes=COALESCE(@Notes,notes),
                trophyPhotoStorageReference=COALESCE(@Photo,trophyPhotoStorageReference), reviewNotes=NULL, updatedAt=SYSUTCDATETIME()
            WHERE submissionId=@Id AND hunterUserId=@UserId AND status='CorrectionRequested';
            """;
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Id", SqlDbType.BigInt).Value = submissionId;
        command.Parameters.Add("@UserId", SqlDbType.Int).Value = hunterUserId;
        command.Parameters.Add("@Notes", SqlDbType.NVarChar, -1).Value = Db(request.Notes?.Trim());
        command.Parameters.Add("@Photo", SqlDbType.NVarChar, 700).Value = Db(request.TrophyPhotoPath?.Trim());
        await connection.OpenAsync(cancellationToken);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private const string SelectSql = """
        SELECT s.submissionId,s.bookingId,ISNULL(p.pTitle,CONCAT('Booking #',s.bookingId)),s.hunterUserId,
               LTRIM(RTRIM(ISNULL(u.uName,'')+' '+ISNULL(u.uSurname,''))),s.species,s.trophyPhotoStorageReference,
               s.antlerSizeCm,s.carcassWeightKg,s.harvestDate,s.locationText,s.shotDistanceMeters,s.firearmType,s.notes,
               s.supportingMediaIdsJson,s.status,s.reviewNotes,s.createdAt,s.updatedAt,s.rowlandWardRequestJson
        FROM dbo.trophySubmissions s JOIN dbo.bookings b ON b.bookingId=s.bookingId
        JOIN dbo.users u ON u.userId=s.hunterUserId LEFT JOIN dbo.packages p ON p.packageId=b.bookPackageId
        """;

    private static TrophySubmissionDto Read(SqlDataReader reader)
    {
        var media = reader.IsDBNull(14) ? [] : JsonSerializer.Deserialize<List<long>>(reader.GetString(14), JsonDefaults.Options) ?? [];
        return new TrophySubmissionDto
        {
            SubmissionId=reader.GetInt64(0),BookingId=reader.GetInt32(1),BookingTitle=reader.GetString(2),HunterUserId=reader.GetInt32(3),HunterName=reader.GetString(4),
            Species=reader.GetString(5),TrophyPhotoPath=$"/api/trophy-submissions/{reader.GetInt64(0)}/photo",TrophyPhotoStorageReference=reader.GetString(6),AntlerSizeCm=reader.IsDBNull(7)?null:reader.GetDecimal(7),CarcassWeightKg=reader.IsDBNull(8)?null:reader.GetDecimal(8),
            HarvestDate=reader.GetDateTime(9),LocationText=reader.GetString(10),ShotDistanceMeters=reader.IsDBNull(11)?null:reader.GetInt32(11),FirearmType=reader.GetString(12),Notes=reader.IsDBNull(13)?null:reader.GetString(13),
            SupportingMediaIds=media,SupportingPhotoCount=media.Count,Status=reader.GetString(15),ReviewNotes=reader.IsDBNull(16)?null:reader.GetString(16),CreatedAt=reader.GetDateTime(17),UpdatedAt=reader.GetDateTime(18)
        };
    }

    private static object Db(object? value) => value ?? DBNull.Value;
}
