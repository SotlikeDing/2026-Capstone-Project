using System.Data;
using Microsoft.Data.SqlClient;
using MobileApi.Models;
using MobileApi.Models.RowlandWard;

namespace MobileApi.Database;

public class Trophies_db : ITrophiesRepository
{
    private readonly string _connectionString;

    public Trophies_db(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<int> CreateAsync(
        int ownerUserId,
        TrophySaveRequest request,
        RowlandWardEvaluationSnapshot? evaluation = null)
    {
        const string sql = """
            INSERT INTO dbo.trophies (
                tOwnerUserId,
                tSpecies,
                tPhotoPath,
                tAntlerSize,
                tCarcassWeight,
                tShotDistanceMeters,
                tFirearmType,
                tHarvestDate,
                tLocationText,
                tNotes,
                tIsPublic,
                tRowlandWardClassId,
                tRowlandWardSpeciesNameAf
            )
            VALUES (
                @OwnerUserId,
                @Species,
                @PhotoPath,
                @AntlerSize,
                @CarcassWeight,
                @ShotDistanceMeters,
                @FirearmType,
                @HarvestDate,
                @LocationText,
                @Notes,
                @IsPublic,
                @RowlandWardClassId,
                @RowlandWardSpeciesNameAf
            );

            SELECT CONVERT(INT, SCOPE_IDENTITY());
            """;

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();

        try
        {
            await using var command = new SqlCommand(sql, connection, transaction);
            AddSaveParameters(command, ownerUserId, request, evaluation);
            var trophyId = (int)(await command.ExecuteScalarAsync())!;

            if (evaluation is not null)
            {
                evaluation.EvaluationId = await RowlandWardEvaluationSql.InsertEvaluationAsync(
                    connection,
                    transaction,
                    trophyId,
                    evaluation
                );
            }

            await transaction.CommitAsync();
            return trophyId;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<List<Trophy>> GetForOwnerAsync(int ownerUserId)
    {
        var sql = $"""
            SELECT
                t.trophyId,
                t.tOwnerUserId,
                t.tSpecies,
                t.tPhotoPath,
                t.tAntlerSize,
                t.tCarcassWeight,
                t.tShotDistanceMeters,
                t.tFirearmType,
                t.tHarvestDate,
                t.tLocationText,
                t.tNotes,
                t.tIsPublic,
                t.tCreatedTimestamp,
                t.tUpdatedTimestamp,
                {RowlandWardEvaluationSql.OwnerSelectColumns}
            FROM dbo.trophies AS t
            LEFT JOIN dbo.trophyRowlandWardEvaluations AS e
              ON e.trweTrophyId = t.trophyId
             AND e.trweIsCurrent = 1
            WHERE t.tOwnerUserId = @OwnerUserId
              AND t.tApprovalStatus = 'Approved'
            ORDER BY t.tCreatedTimestamp DESC, t.trophyId DESC;

            {MeasurementSelect}
            WHERE t.tOwnerUserId = @OwnerUserId
              AND t.tApprovalStatus = 'Approved'
              AND e.trweIsCurrent = 1
            ORDER BY m.trwmEvaluationId, m.trophyRowlandWardMeasurementId;
            """;

        var trophies = new List<Trophy>();
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@OwnerUserId", SqlDbType.Int).Value = ownerUserId;

        await connection.OpenAsync();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            trophies.Add(ReadTrophy(reader));
        }

        await LoadMeasurementsAsync(reader, trophies);
        return trophies;
    }

    public async Task<Trophy?> GetByIdForOwnerAsync(int trophyId, int ownerUserId)
    {
        var sql = $"""
            SELECT
                t.trophyId,
                t.tOwnerUserId,
                t.tSpecies,
                t.tPhotoPath,
                t.tAntlerSize,
                t.tCarcassWeight,
                t.tShotDistanceMeters,
                t.tFirearmType,
                t.tHarvestDate,
                t.tLocationText,
                t.tNotes,
                t.tIsPublic,
                t.tCreatedTimestamp,
                t.tUpdatedTimestamp,
                {RowlandWardEvaluationSql.OwnerSelectColumns}
            FROM dbo.trophies AS t
            LEFT JOIN dbo.trophyRowlandWardEvaluations AS e
              ON e.trweTrophyId = t.trophyId
             AND e.trweIsCurrent = 1
            WHERE t.trophyId = @TrophyId
              AND t.tOwnerUserId = @OwnerUserId
              AND t.tApprovalStatus = 'Approved';

            {MeasurementSelect}
            WHERE t.trophyId = @TrophyId
              AND t.tOwnerUserId = @OwnerUserId
              AND t.tApprovalStatus = 'Approved'
              AND e.trweIsCurrent = 1
            ORDER BY m.trophyRowlandWardMeasurementId;
            """;

        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@TrophyId", SqlDbType.Int).Value = trophyId;
        command.Parameters.Add("@OwnerUserId", SqlDbType.Int).Value = ownerUserId;

        await connection.OpenAsync();
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        var trophy = ReadTrophy(reader);
        await LoadMeasurementsAsync(reader, [trophy]);
        return trophy;
    }

    public async Task<bool> IsPhotoPublicAsync(
        string photoPath,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT CASE WHEN EXISTS (
                SELECT 1
                FROM dbo.trophies
                WHERE tPhotoPath = @PhotoPath
                  AND tIsPublic = 1
                  AND tApprovalStatus = 'Approved'
            ) THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END;
            """;

        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@PhotoPath", SqlDbType.NVarChar, 500).Value = photoPath;

        await connection.OpenAsync(cancellationToken);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    public async Task<bool> UpdateForOwnerAsync(
        int trophyId,
        int ownerUserId,
        TrophySaveRequest request,
        RowlandWardEvaluationSnapshot? evaluation = null)
    {
        const string sql = """
            UPDATE dbo.trophies
            SET
                tSpecies = CASE
                    WHEN @ReplaceRowlandWard = 0 AND tRowlandWardClassId IS NOT NULL
                        THEN tSpecies
                    ELSE @Species
                END,
                tPhotoPath = @PhotoPath,
                tAntlerSize = @AntlerSize,
                tCarcassWeight = @CarcassWeight,
                tShotDistanceMeters = @ShotDistanceMeters,
                tFirearmType = @FirearmType,
                tHarvestDate = @HarvestDate,
                tLocationText = @LocationText,
                tNotes = @Notes,
                tIsPublic = @IsPublic,
                tRowlandWardClassId = CASE
                    WHEN @ReplaceRowlandWard = 1 THEN @RowlandWardClassId
                    ELSE tRowlandWardClassId
                END,
                tRowlandWardSpeciesNameAf = CASE
                    WHEN @ReplaceRowlandWard = 1 THEN @RowlandWardSpeciesNameAf
                    ELSE tRowlandWardSpeciesNameAf
                END,
                tUpdatedTimestamp = SYSUTCDATETIME()
            WHERE trophyId = @TrophyId
              AND tOwnerUserId = @OwnerUserId;
            """;

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();

        try
        {
            await using var command = new SqlCommand(sql, connection, transaction);
            command.Parameters.Add("@TrophyId", SqlDbType.Int).Value = trophyId;
            command.Parameters.Add("@ReplaceRowlandWard", SqlDbType.Bit).Value = evaluation is not null;
            AddSaveParameters(command, ownerUserId, request, evaluation);
            var updated = await command.ExecuteNonQueryAsync() == 1;
            if (!updated)
            {
                await transaction.RollbackAsync();
                return false;
            }

            if (evaluation is not null)
            {
                const string retireSql = """
                    UPDATE dbo.trophyRowlandWardEvaluations
                    SET trweIsCurrent = 0
                    WHERE trweTrophyId = @TrophyId
                      AND trweIsCurrent = 1;
                    """;
                await using var retireCommand = new SqlCommand(retireSql, connection, transaction);
                retireCommand.Parameters.Add("@TrophyId", SqlDbType.Int).Value = trophyId;
                await retireCommand.ExecuteNonQueryAsync();

                evaluation.EvaluationId = await RowlandWardEvaluationSql.InsertEvaluationAsync(
                    connection,
                    transaction,
                    trophyId,
                    evaluation
                );
            }

            await transaction.CommitAsync();
            return true;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<bool> DeleteForOwnerAsync(int trophyId, int ownerUserId)
    {
        const string sql = """
            DELETE FROM dbo.trophies
            WHERE trophyId = @TrophyId
              AND tOwnerUserId = @OwnerUserId;
            """;

        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@TrophyId", SqlDbType.Int).Value = trophyId;
        command.Parameters.Add("@OwnerUserId", SqlDbType.Int).Value = ownerUserId;

        await connection.OpenAsync();
        return await command.ExecuteNonQueryAsync() > 0;
    }

    private const string MeasurementSelect = """
        SELECT
            m.trwmEvaluationId,
            m.trwmFieldId,
            m.trwmLabelAf,
            m.trwmKind,
            m.trwmRole,
            m.trwmInputNumerator,
            m.trwmInputDenominator,
            m.trwmInputUnitCode,
            m.trwmCanonicalNumerator,
            m.trwmCanonicalDenominator,
            m.trwmCanonicalUnitCode,
            m.trwmScoringNumerator,
            m.trwmScoringDenominator,
            m.trwmScoringUnitCode,
            m.trwmIntegerValue,
            m.trwmBooleanValue
        FROM dbo.trophyRowlandWardMeasurements AS m
        INNER JOIN dbo.trophyRowlandWardEvaluations AS e
          ON e.trophyRowlandWardEvaluationId = m.trwmEvaluationId
        INNER JOIN dbo.trophies AS t
          ON t.trophyId = e.trweTrophyId
        """;

    private static async Task LoadMeasurementsAsync(
        SqlDataReader reader,
        IReadOnlyList<Trophy> trophies)
    {
        var evaluations = trophies
            .Where(trophy => trophy.RowlandWard is not null)
            .ToDictionary(trophy => trophy.RowlandWard!.EvaluationId, trophy => trophy.RowlandWard!);

        if (!await reader.NextResultAsync())
        {
            return;
        }

        while (await reader.ReadAsync())
        {
            var (evaluationId, measurement) = RowlandWardEvaluationSql.ReadMeasurement(reader);
            if (evaluations.TryGetValue(evaluationId, out var evaluation))
            {
                evaluation.Measurements.Add(measurement);
            }
        }
    }

    private static void AddSaveParameters(
        SqlCommand command,
        int ownerUserId,
        TrophySaveRequest request,
        RowlandWardEvaluationSnapshot? evaluation)
    {
        command.Parameters.Add("@OwnerUserId", SqlDbType.Int).Value = ownerUserId;
        command.Parameters.Add("@Species", SqlDbType.NVarChar, 120).Value = request.Species.Trim();
        command.Parameters.Add("@PhotoPath", SqlDbType.NVarChar, 500).Value = request.PhotoPath.Trim();
        AddNullableDecimal(command, "@AntlerSize", request.AntlerSizeCm);
        AddNullableDecimal(command, "@CarcassWeight", request.CarcassWeightKg);
        command.Parameters.Add("@ShotDistanceMeters", SqlDbType.Int).Value =
            request.ShotDistanceMeters ?? (object)DBNull.Value;
        command.Parameters.Add("@FirearmType", SqlDbType.NVarChar, 120).Value = request.FirearmType.Trim();
        command.Parameters.Add("@HarvestDate", SqlDbType.Date).Value = request.HarvestDate.Date;
        command.Parameters.Add("@LocationText", SqlDbType.NVarChar, 200).Value = request.LocationText.Trim();
        command.Parameters.Add("@Notes", SqlDbType.NVarChar, -1).Value =
            string.IsNullOrWhiteSpace(request.Notes) ? DBNull.Value : request.Notes.Trim();
        command.Parameters.Add("@IsPublic", SqlDbType.Bit).Value = request.IsPublic;
        command.Parameters.Add("@RowlandWardClassId", SqlDbType.NVarChar, 160).Value =
            evaluation is null ? DBNull.Value : evaluation.ClassId;
        command.Parameters.Add("@RowlandWardSpeciesNameAf", SqlDbType.NVarChar, 160).Value =
            evaluation is null ? DBNull.Value : evaluation.DisplayNameAf;
    }

    private static void AddNullableDecimal(SqlCommand command, string name, decimal? value)
    {
        var parameter = command.Parameters.Add(name, SqlDbType.Decimal);
        parameter.Precision = 10;
        parameter.Scale = 2;
        parameter.Value = value ?? (object)DBNull.Value;
    }

    private static Trophy ReadTrophy(SqlDataReader reader)
    {
        return new Trophy
        {
            Id = reader.GetInt32(reader.GetOrdinal("trophyId")),
            OwnerUserId = reader.GetInt32(reader.GetOrdinal("tOwnerUserId")),
            Species = reader.GetString(reader.GetOrdinal("tSpecies")),
            PhotoPath = reader.GetString(reader.GetOrdinal("tPhotoPath")),
            AntlerSizeCm = reader.IsDBNull(reader.GetOrdinal("tAntlerSize"))
                ? null
                : reader.GetDecimal(reader.GetOrdinal("tAntlerSize")),
            CarcassWeightKg = reader.IsDBNull(reader.GetOrdinal("tCarcassWeight"))
                ? null
                : reader.GetDecimal(reader.GetOrdinal("tCarcassWeight")),
            ShotDistanceMeters = reader.IsDBNull(reader.GetOrdinal("tShotDistanceMeters"))
                ? null
                : reader.GetInt32(reader.GetOrdinal("tShotDistanceMeters")),
            FirearmType = reader.GetString(reader.GetOrdinal("tFirearmType")),
            HarvestDate = reader.GetDateTime(reader.GetOrdinal("tHarvestDate")),
            LocationText = reader.GetString(reader.GetOrdinal("tLocationText")),
            Notes = reader.IsDBNull(reader.GetOrdinal("tNotes"))
                ? null
                : reader.GetString(reader.GetOrdinal("tNotes")),
            IsPublic = reader.GetBoolean(reader.GetOrdinal("tIsPublic")),
            CreatedTimestamp = reader.GetDateTime(reader.GetOrdinal("tCreatedTimestamp")),
            UpdatedTimestamp = reader.GetDateTime(reader.GetOrdinal("tUpdatedTimestamp")),
            RowlandWard = RowlandWardEvaluationSql.ReadOwnerEvaluation(reader)
        };
    }
}
