using Microsoft.Data.SqlClient;
using WebAPI.Models;

namespace WebAPI.Database;

/// <summary>Read-only reference-data queries for static/lookup lists shown on booking and farm-management forms.</summary>
public class Extra_db
{
    private readonly string _connectionString;
    public Extra_db(string connectionString) => _connectionString = connectionString;

    /// <summary>Taxidermy service providers - actually rows in the shared advertisements table filtered to adType = 'Taxidermy'.</summary>
    public async Task<List<TaxidermyResponse>> GetTaxidermy()
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        string q = "SELECT advertisementId, adTitle FROM advertisements WHERE adType = 'Taxidermy'";
        using var cmd = new SqlCommand(q, conn);
        using var r = await cmd.ExecuteReaderAsync();
        var list = new List<TaxidermyResponse>();

        while (await r.ReadAsync())
        {
            list.Add(new TaxidermyResponse
            {
                Id = r.GetInt32(0),
                Title = r.GetString(1)
            });
        }
        return list;
    }

    /// <summary>Slaughterhouse/processing facilities - same advertisements table, filtered to adType = 'Slaughterhouse'.</summary>
    public async Task<List<SlaughterhouseResponse>> GetSlaughterhouses()
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        string q = "SELECT advertisementId, adTitle FROM advertisements WHERE adType = 'Slaughterhouse'";
        using var cmd = new SqlCommand(q, conn);
        using var r = await cmd.ExecuteReaderAsync();
        var list = new List<SlaughterhouseResponse>();

        while (await r.ReadAsync())
        {
            list.Add(new SlaughterhouseResponse
            {
                Id = r.GetInt32(0),
                Title = r.GetString(1)
            });
        }
        return list;
    }

    /// <summary>
    /// "Dangerous" game species - there is no dedicated dangerous-game flag; this
    /// uses aaQuota IS NOT NULL on availableAnimals as a proxy, since quota-limited
    /// species happen to be the regulated dangerous-game ones in this dataset.
    /// </summary>
    public async Task<List<DangerousAnimalResponse>> GetDangerousAnimals()
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        string q = "SELECT aaId, aaName, aaQuota FROM availableAnimals WHERE aaQuota IS NOT NULL";
        using var cmd = new SqlCommand(q, conn);
        using var r = await cmd.ExecuteReaderAsync();
        var list = new List<DangerousAnimalResponse>();

        while (await r.ReadAsync())
        {
            list.Add(new DangerousAnimalResponse
            {
                Id = r.GetInt32(0),
                Name = r.GetString(1),
                Quota = r.IsDBNull(2) ? (long?)null : r.GetInt64(2)
            });
        }
        return list;
    }

    /// <summary>Lists South African provinces alphabetically, for address/location dropdowns.</summary>
    public async Task<List<Province>> GetProvinces()
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        string q = "SELECT provId, provName FROM provinces ORDER BY provName";
        using var cmd = new SqlCommand(q, conn);
        using var r = await cmd.ExecuteReaderAsync();
        var list = new List<Province>();
        while (await r.ReadAsync())
        {
            list.Add(new Province { Id = r.GetInt32(0), Name = r.GetString(1) });
        }
        return list;
    }

}
