using Microsoft.Data.SqlClient;
using WebAPI.Models;

namespace WebAPI.Database;

/// <summary>Admin-controlled on/off feature flags stored in the featureFlags table.</summary>
public class FeatureFlags_db
{
    private readonly string _connectionString;

    public FeatureFlags_db(string connectionString) => _connectionString = connectionString;

    /// <summary>Lists every feature flag, alphabetically by key.</summary>
    public virtual async Task<List<FeatureFlagDto>> GetAllAsync()
    {
        var results = new List<FeatureFlagDto>();

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        string query = @"SELECT flagKey, flagDescription, flagIsEnabled, flagLastUpdated, flagUpdatedBy FROM featureFlags ORDER BY flagKey;";
        using var cmd = new SqlCommand(query, conn);
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new FeatureFlagDto
            {
                Key = reader.GetString(0),
                Description = reader.GetString(1),
                IsEnabled = reader.GetBoolean(2),
                LastUpdated = reader.GetDateTime(3),
                UpdatedBy = reader.GetString(4)
            });
        }

        return results;
    }

    /// <summary>Sets a flag's enabled state and stamps who changed it and when. Returns null if the key doesn't exist.</summary>
    public virtual async Task<FeatureFlagDto?> ToggleAsync(string key, bool isEnabled, string updatedBy)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        string query = @"UPDATE featureFlags
                          SET flagIsEnabled = @isEnabled, flagLastUpdated = SYSUTCDATETIME(), flagUpdatedBy = @updatedBy
                          OUTPUT INSERTED.flagKey, INSERTED.flagDescription, INSERTED.flagIsEnabled, INSERTED.flagLastUpdated, INSERTED.flagUpdatedBy
                          WHERE flagKey = @key;";

        using var cmd = new SqlCommand(query, conn);
        cmd.Parameters.AddWithValue("@key", key);
        cmd.Parameters.AddWithValue("@isEnabled", isEnabled);
        cmd.Parameters.AddWithValue("@updatedBy", updatedBy);

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new FeatureFlagDto
            {
                Key = reader.GetString(0),
                Description = reader.GetString(1),
                IsEnabled = reader.GetBoolean(2),
                LastUpdated = reader.GetDateTime(3),
                UpdatedBy = reader.GetString(4)
            };
        }
        return null;
    }
}
