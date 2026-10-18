using Microsoft.Data.SqlClient;
using WebAPI.Models;

namespace WebAPI.Database;

/// <summary>Read-only queries backing the public marketing landing page.</summary>
public class Landing_db
{
    private readonly string _connectionString;
    public Landing_db(string connectionString) => _connectionString = connectionString;

    /// <summary>Fetches the most recently received "Contact Us" submission, for display purposes.</summary>
    public async Task<ContactResponse?> GetContact()
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        string q = "SELECT TOP 1 cuSubject, cuContent, cuSenderEmail FROM contactUs ORDER BY contactUsId DESC";
        using var cmd = new SqlCommand(q, conn);
        using var r = await cmd.ExecuteReaderAsync();

        if (await r.ReadAsync())
        {
            return new ContactResponse
            {
                Subject = r.GetString(0),
                Content = r.GetString(1),
                Email = r.GetString(2)
            };
        }
        return null;
    }

    /// <summary>The 10 most recently created packages, for the landing page's featured section.</summary>
    public async Task<List<PackageResponse>> GetPackages()
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        string q = "SELECT TOP 10 packageId, pTitle, pDescription FROM packages ORDER BY pCreatedTimestamp DESC";
        using var cmd = new SqlCommand(q, conn);
        using var r = await cmd.ExecuteReaderAsync();
        var list = new List<PackageResponse>();

        while (await r.ReadAsync())
        {
            list.Add(new PackageResponse
            {
                Id = r.GetInt32(0),
                Title = r.GetString(1),
                Description = r.IsDBNull(2) ? string.Empty : r.GetString(2)
            });
        }
        return list;
    }

    /// <summary>Top 20 rated users by average star rating (across all rating contexts, unfiltered by role), for the leaderboard widget.</summary>
    public async Task<List<LeaderboardEntry>> GetLeaderboard()
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        string q = @"SELECT TOP 20 rateeUserId, AVG(CAST(stars AS DECIMAL(3,2))) as avgStars
                     FROM ratings
                     GROUP BY rateeUserId
                     ORDER BY avgStars DESC";
        using var cmd = new SqlCommand(q, conn);
        using var r = await cmd.ExecuteReaderAsync();
        var list = new List<LeaderboardEntry>();

        while (await r.ReadAsync())
        {
            list.Add(new LeaderboardEntry
            {
                RateeId = r.GetInt32(0),
                AverageStars = r.GetDecimal(1)
            });
        }
        return list;
    }
}
