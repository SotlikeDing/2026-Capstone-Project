using System.Data;
using Microsoft.Data.SqlClient;
using MobileApi.Models;

namespace MobileApi.Database;

public class PublicTrophies_db : IPublicTrophiesRepository
{
    public const int LeaderboardMaxRows = 500;

    private readonly string _connectionString;

    public PublicTrophies_db(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<List<PublicTrophySummary>> GetPublicTrophiesAsync()
    {
        const string sql = """
            SELECT
                t.trophyId,
                t.tPhotoPath,
                CASE
                    WHEN t.tRowlandWardClassId IS NOT NULL
                        THEN COALESCE(
                            NULLIF(LTRIM(RTRIM(t.tRowlandWardSpeciesNameAf)), ''),
                            t.tSpecies
                        )
                    ELSE t.tSpecies
                END AS tSpecies,
                t.tAntlerSize,
                t.tCarcassWeight,
                t.tShotDistanceMeters,
                t.tFirearmType,
                t.tHarvestDate,
                NULLIF(LTRIM(RTRIM(CONCAT(u.uName, ' ', u.uSurname))), '') AS OwnerDisplayName,
                e.trophyRowlandWardEvaluationId AS rweEvaluationId,
                e.trweClassId AS rweClassId,
                e.trweDisplayNameAf AS rweDisplayNameAf,
                e.trweMethodCode AS rweMethodCode,
                e.trweSourceVersionId AS rweSourceVersionId,
                e.trweQualificationState AS rweQualificationState,
                e.trweMinimumReached AS rweMinimumReached,
                e.trweScoreDisplayNumerator AS rweScoreDisplayNumerator,
                e.trweScoreDisplayDenominator AS rweScoreDisplayDenominator,
                e.trweScoreDisplayUnitCode AS rweScoreDisplayUnitCode
            FROM dbo.trophies AS t
            INNER JOIN dbo.users AS u ON u.userId = t.tOwnerUserId
            LEFT JOIN dbo.trophyRowlandWardEvaluations AS e
              ON e.trweTrophyId = t.trophyId
             AND e.trweIsCurrent = 1
            WHERE t.tIsPublic = 1 AND t.tApprovalStatus = 'Approved';
            """;

        var trophies = new List<PublicTrophySummary>();

        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand(sql, connection);
        await connection.OpenAsync();
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            trophies.Add(ReadSummary(reader));
        }

        return trophies;
    }

    public async Task<List<string>> GetPublicSpeciesAsync()
    {
        const string sql = """
            SELECT DISTINCT
                CASE
                    WHEN tRowlandWardClassId IS NOT NULL
                        THEN COALESCE(
                            NULLIF(LTRIM(RTRIM(tRowlandWardSpeciesNameAf)), ''),
                            LTRIM(RTRIM(tSpecies))
                        )
                    ELSE LTRIM(RTRIM(tSpecies))
                END AS FilterValue
            FROM dbo.trophies
            WHERE tIsPublic = 1 AND tApprovalStatus = 'Approved'
              AND NULLIF(
                    CASE
                        WHEN tRowlandWardClassId IS NOT NULL
                            THEN COALESCE(
                                NULLIF(LTRIM(RTRIM(tRowlandWardSpeciesNameAf)), ''),
                                LTRIM(RTRIM(tSpecies))
                            )
                        ELSE LTRIM(RTRIM(tSpecies))
                    END,
                    ''
                  ) IS NOT NULL
            ORDER BY FilterValue ASC;
            """;

        return await GetFilterValuesAsync(sql);
    }

    public async Task<List<string>> GetPublicFirearmTypesAsync()
    {
        const string sql = """
            SELECT DISTINCT LTRIM(RTRIM(tFirearmType)) AS FilterValue
            FROM dbo.trophies
            WHERE tIsPublic = 1 AND tApprovalStatus = 'Approved'
              AND NULLIF(LTRIM(RTRIM(tFirearmType)), '') IS NOT NULL
            ORDER BY FilterValue ASC;
            """;

        return await GetFilterValuesAsync(sql);
    }

    public async Task<List<string>> GetPublicLegacySpeciesAsync()
    {
        const string sql = """
            SELECT DISTINCT LTRIM(RTRIM(tSpecies)) AS FilterValue
            FROM dbo.trophies
            WHERE tIsPublic = 1 AND tApprovalStatus = 'Approved'
              AND tRowlandWardClassId IS NULL
              AND NULLIF(LTRIM(RTRIM(tSpecies)), '') IS NOT NULL
            ORDER BY FilterValue ASC;
            """;

        return await GetFilterValuesAsync(sql);
    }

    public async Task<PublicTrophyDetail?> GetPublicTrophyByIdAsync(int trophyId)
    {
        const string sql = """
            SELECT
                t.trophyId,
                t.tPhotoPath,
                CASE
                    WHEN t.tRowlandWardClassId IS NOT NULL
                        THEN COALESCE(
                            NULLIF(LTRIM(RTRIM(t.tRowlandWardSpeciesNameAf)), ''),
                            t.tSpecies
                        )
                    ELSE t.tSpecies
                END AS tSpecies,
                t.tAntlerSize,
                t.tCarcassWeight,
                t.tShotDistanceMeters,
                t.tFirearmType,
                t.tHarvestDate,
                NULLIF(LTRIM(RTRIM(CONCAT(u.uName, ' ', u.uSurname))), '') AS OwnerDisplayName,
                t.tNotes,
                t.tCreatedTimestamp,
                e.trophyRowlandWardEvaluationId AS rweEvaluationId,
                e.trweClassId AS rweClassId,
                e.trweDisplayNameAf AS rweDisplayNameAf,
                e.trweMethodCode AS rweMethodCode,
                e.trweSourceVersionId AS rweSourceVersionId,
                e.trweQualificationState AS rweQualificationState,
                e.trweMinimumReached AS rweMinimumReached,
                e.trweScoreDisplayNumerator AS rweScoreDisplayNumerator,
                e.trweScoreDisplayDenominator AS rweScoreDisplayDenominator,
                e.trweScoreDisplayUnitCode AS rweScoreDisplayUnitCode
            FROM dbo.trophies AS t
            INNER JOIN dbo.users AS u ON u.userId = t.tOwnerUserId
            LEFT JOIN dbo.trophyRowlandWardEvaluations AS e
              ON e.trweTrophyId = t.trophyId
             AND e.trweIsCurrent = 1
            WHERE t.trophyId = @TrophyId
              AND t.tIsPublic = 1
              AND t.tApprovalStatus = 'Approved';
            """;

        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@TrophyId", SqlDbType.Int).Value = trophyId;

        await connection.OpenAsync();
        await using var reader = await command.ExecuteReaderAsync();

        return await reader.ReadAsync() ? ReadDetail(reader) : null;
    }

    public async Task<List<LeaderboardTrophySummary>> GetLeaderboardAsync(
        IReadOnlyCollection<string>? rowlandWardClassIds,
        bool legacyOnly,
        string? legacySpecies,
        string? firearmType)
    {
        var whereClauses = new List<string>
        {
            "t.tIsPublic = 1",
            "t.tApprovalStatus = 'Approved'"
        };

        if (legacyOnly)
        {
            whereClauses.Add("t.tRowlandWardClassId IS NULL");
            if (legacySpecies is not null)
            {
                whereClauses.Add("LTRIM(RTRIM(t.tSpecies)) = @LegacySpecies");
            }
        }
        else if (rowlandWardClassIds is not null)
        {
            if (rowlandWardClassIds.Count == 0)
            {
                return [];
            }

            var classParameters = rowlandWardClassIds
                .Select((_, index) => $"@ClassId{index}")
                .ToArray();
            whereClauses.Add(
                $"t.tRowlandWardClassId IN ({string.Join(", ", classParameters)})"
            );
        }

        if (firearmType is not null)
        {
            whereClauses.Add("LTRIM(RTRIM(t.tFirearmType)) = @FirearmType");
        }

        var legacyFirearmFilter = firearmType is null
            ? string.Empty
            : "AND LTRIM(RTRIM(candidate.tFirearmType)) = @FirearmType";
        var rankExpression = $"""
            CASE
                WHEN t.tRowlandWardClassId IS NOT NULL THEN
                    CASE
                        WHEN e.trweScoreDisplayDenominator IS NOT NULL
                         AND e.trweScoreDisplayDenominator <> 0
                            THEN CAST(e.trweScoreDisplayNumerator AS DECIMAL(28, 10))
                               / CAST(e.trweScoreDisplayDenominator AS DECIMAL(28, 10))
                    END
                WHEN EXISTS (
                    SELECT 1
                    FROM dbo.trophies AS candidate
                    WHERE candidate.tIsPublic = 1
                      AND candidate.tApprovalStatus = 'Approved'
                      AND candidate.tRowlandWardClassId IS NULL
                      AND LTRIM(RTRIM(candidate.tSpecies)) = LTRIM(RTRIM(t.tSpecies))
                      AND candidate.tAntlerSize IS NOT NULL
                      {legacyFirearmFilter}
                ) THEN CAST(t.tAntlerSize AS DECIMAL(28, 10))
                ELSE CAST(t.tCarcassWeight AS DECIMAL(28, 10))
            END
            """;

        // Bounded so one request can't pull every public trophy in the system
        // into a phone's memory; the app tells the user when the cap is hit.
        var sql = $"""
            SELECT TOP ({LeaderboardMaxRows})
                t.trophyId,
                t.tPhotoPath,
                CASE
                    WHEN t.tRowlandWardClassId IS NOT NULL
                        THEN COALESCE(
                            NULLIF(LTRIM(RTRIM(t.tRowlandWardSpeciesNameAf)), ''),
                            t.tSpecies
                        )
                    ELSE t.tSpecies
                END AS tSpecies,
                t.tAntlerSize,
                t.tCarcassWeight,
                t.tShotDistanceMeters,
                t.tFirearmType,
                t.tHarvestDate,
                NULLIF(LTRIM(RTRIM(CONCAT(u.uName, ' ', u.uSurname))), '') AS OwnerDisplayName,
                {rankExpression} AS SelectedSortValue,
                e.trophyRowlandWardEvaluationId AS rweEvaluationId,
                e.trweClassId AS rweClassId,
                e.trweDisplayNameAf AS rweDisplayNameAf,
                e.trweMethodCode AS rweMethodCode,
                e.trweSourceVersionId AS rweSourceVersionId,
                e.trweQualificationState AS rweQualificationState,
                e.trweMinimumReached AS rweMinimumReached,
                e.trweScoreDisplayNumerator AS rweScoreDisplayNumerator,
                e.trweScoreDisplayDenominator AS rweScoreDisplayDenominator,
                e.trweScoreDisplayUnitCode AS rweScoreDisplayUnitCode
            FROM dbo.trophies AS t
            INNER JOIN dbo.users AS u ON u.userId = t.tOwnerUserId
            LEFT JOIN dbo.trophyRowlandWardEvaluations AS e
              ON e.trweTrophyId = t.trophyId
             AND e.trweIsCurrent = 1
            WHERE {string.Join("\n              AND ", whereClauses)}
            ORDER BY
                CASE WHEN t.tRowlandWardClassId IS NULL THEN 1 ELSE 0 END ASC,
                CASE
                    WHEN t.tRowlandWardClassId IS NOT NULL
                        THEN COALESCE(
                            NULLIF(LTRIM(RTRIM(t.tRowlandWardSpeciesNameAf)), ''),
                            LTRIM(RTRIM(t.tSpecies))
                        )
                    ELSE LTRIM(RTRIM(t.tSpecies))
                END ASC,
                CASE WHEN {rankExpression} IS NULL THEN 1 ELSE 0 END ASC,
                {rankExpression} DESC,
                t.trophyId DESC;
            """;

        var trophies = new List<LeaderboardTrophySummary>();

        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand(sql, connection);
        if (rowlandWardClassIds is not null)
        {
            var classIndex = 0;
            foreach (var classId in rowlandWardClassIds)
            {
                command.Parameters
                    .Add($"@ClassId{classIndex}", SqlDbType.NVarChar, 160)
                    .Value = classId;
                classIndex++;
            }
        }

        if (legacySpecies is not null)
        {
            command.Parameters.Add("@LegacySpecies", SqlDbType.NVarChar, 120).Value =
                legacySpecies;
        }

        if (firearmType is not null)
        {
            command.Parameters.Add("@FirearmType", SqlDbType.NVarChar, 120).Value =
                firearmType;
        }

        await connection.OpenAsync();
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            trophies.Add(ReadLeaderboardSummary(reader));
        }

        return trophies;
    }

    private async Task<List<string>> GetFilterValuesAsync(string sql)
    {
        var values = new List<string>();

        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand(sql, connection);
        await connection.OpenAsync();
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(reader.GetOrdinal("FilterValue")));
        }

        return values;
    }

    private static PublicTrophySummary ReadSummary(SqlDataReader reader)
    {
        return new PublicTrophySummary
        {
            Id = reader.GetInt32(reader.GetOrdinal("trophyId")),
            PhotoPath = reader.GetString(reader.GetOrdinal("tPhotoPath")),
            Species = reader.GetString(reader.GetOrdinal("tSpecies")),
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
            OwnerDisplayName = reader.IsDBNull(reader.GetOrdinal("OwnerDisplayName"))
                ? null
                : reader.GetString(reader.GetOrdinal("OwnerDisplayName")),
            StoredRowlandWardEvaluation = RowlandWardEvaluationSql.ReadPublicEvaluation(reader)
        };
    }

    private static PublicTrophyDetail ReadDetail(SqlDataReader reader)
    {
        var summary = ReadSummary(reader);

        return new PublicTrophyDetail
        {
            Id = summary.Id,
            PhotoPath = summary.PhotoPath,
            Species = summary.Species,
            AntlerSizeCm = summary.AntlerSizeCm,
            CarcassWeightKg = summary.CarcassWeightKg,
            ShotDistanceMeters = summary.ShotDistanceMeters,
            FirearmType = summary.FirearmType,
            HarvestDate = summary.HarvestDate,
            OwnerDisplayName = summary.OwnerDisplayName,
            StoredRowlandWardEvaluation = summary.StoredRowlandWardEvaluation,
            Notes = reader.IsDBNull(reader.GetOrdinal("tNotes"))
                ? null
                : reader.GetString(reader.GetOrdinal("tNotes")),
            CreatedTimestamp = reader.GetDateTime(reader.GetOrdinal("tCreatedTimestamp"))
        };
    }

    private static LeaderboardTrophySummary ReadLeaderboardSummary(SqlDataReader reader)
    {
        var summary = ReadSummary(reader);

        return new LeaderboardTrophySummary
        {
            Id = summary.Id,
            PhotoPath = summary.PhotoPath,
            Species = summary.Species,
            AntlerSizeCm = summary.AntlerSizeCm,
            CarcassWeightKg = summary.CarcassWeightKg,
            ShotDistanceMeters = summary.ShotDistanceMeters,
            FirearmType = summary.FirearmType,
            HarvestDate = summary.HarvestDate,
            OwnerDisplayName = summary.OwnerDisplayName,
            StoredRowlandWardEvaluation = summary.StoredRowlandWardEvaluation,
            SelectedSortValue = reader.IsDBNull(
                reader.GetOrdinal("SelectedSortValue")
            )
                ? null
                : reader.GetDecimal(reader.GetOrdinal("SelectedSortValue"))
        };
    }
}

