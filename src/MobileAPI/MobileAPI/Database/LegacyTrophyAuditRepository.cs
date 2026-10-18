using Microsoft.Data.SqlClient;
using MobileApi.Models.RowlandWard;

namespace MobileApi.Database;

public sealed class LegacyTrophyAuditRepository : ILegacyTrophyAuditRepository
{
    private readonly string _connectionString;

    public LegacyTrophyAuditRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<IReadOnlyList<LegacyTrophySpeciesValue>> GetDistinctLegacySpeciesAsync()
    {
        const string sql = """
            SELECT
                tSpecies AS OriginalSpeciesText,
                COUNT_BIG(*) AS TrophyCount
            FROM dbo.trophies
            WHERE tRowlandWardClassId IS NULL
              AND NULLIF(LTRIM(RTRIM(tSpecies)), '') IS NOT NULL
            GROUP BY tSpecies
            ORDER BY LTRIM(RTRIM(tSpecies)), tSpecies;
            """;

        var values = new List<LegacyTrophySpeciesValue>();

        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand(sql, connection);
        await connection.OpenAsync();
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            values.Add(new LegacyTrophySpeciesValue(
                reader.GetString(0),
                reader.GetInt64(1)));
        }

        return values.AsReadOnly();
    }
}
