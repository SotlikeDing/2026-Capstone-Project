using Microsoft.Data.SqlClient;
using WebAPI.Models;

namespace WebAPI.Database;

/// <summary>Key/value operational and pricing settings (systemSettings table), each flagged public or admin-only.</summary>
public class SystemSettings_db
{
    private readonly string _connectionString;

    public SystemSettings_db(string connectionString) => _connectionString = connectionString;

    /// <summary>Lists every setting, public and admin-only alike.</summary>
    public virtual async Task<List<SystemSettingDto>> GetAllAsync()
    {
        var results = new List<SystemSettingDto>();

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        string query = @"SELECT settingKey, settingValue, settingDescription, isPublic, lastUpdated, updatedBy FROM systemSettings ORDER BY settingKey;";
        using var cmd = new SqlCommand(query, conn);
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(Map(reader));
        }

        return results;
    }

    /// <summary>Lists only settings flagged IsPublic - safe to expose to an unauthenticated visitor.</summary>
    public virtual async Task<List<SystemSettingDto>> GetPublicAsync()
    {
        var results = new List<SystemSettingDto>();

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        string query = @"SELECT settingKey, settingValue, settingDescription, isPublic, lastUpdated, updatedBy FROM systemSettings WHERE isPublic = 1 ORDER BY settingKey;";
        using var cmd = new SqlCommand(query, conn);
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(Map(reader));
        }

        return results;
    }

    /// <summary>Updates a setting's value and stamps who changed it and when. Returns null if the key doesn't exist.</summary>
    public virtual async Task<SystemSettingDto?> SetAsync(string key, string value, string updatedBy)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        string query = @"UPDATE systemSettings
                          SET settingValue = @value, lastUpdated = SYSUTCDATETIME(), updatedBy = @updatedBy
                          OUTPUT INSERTED.settingKey, INSERTED.settingValue, INSERTED.settingDescription, INSERTED.isPublic, INSERTED.lastUpdated, INSERTED.updatedBy
                          WHERE settingKey = @key;";

        using var cmd = new SqlCommand(query, conn);
        cmd.Parameters.AddWithValue("@key", key);
        cmd.Parameters.AddWithValue("@value", value);
        cmd.Parameters.AddWithValue("@updatedBy", updatedBy);

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return Map(reader);
        }
        return null;
    }

    /// <summary>Maps one systemSettings row to its DTO.</summary>
    private static SystemSettingDto Map(SqlDataReader reader) => new()
    {
        Key = reader.GetString(0),
        Value = reader.GetString(1),
        Description = reader.GetString(2),
        IsPublic = reader.GetBoolean(3),
        LastUpdated = reader.GetDateTime(4),
        UpdatedBy = reader.GetString(5)
    };
}
