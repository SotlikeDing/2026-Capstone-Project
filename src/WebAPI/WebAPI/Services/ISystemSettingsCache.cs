using WebAPI.Database;

namespace WebAPI.Services;

/// <summary>In-memory cache of systemSettings values so hot paths (rate limits, JWT lifetime, pricing) don't hit the DB on every request - see SystemSettingsCache.</summary>
public interface ISystemSettingsCache
{
    /// <summary>Reloads every setting from the database.</summary>
    Task RefreshAsync(SystemSettings_db settingsDb);
    /// <summary>Updates one cached value without a full refresh (called right after AdminController persists a change).</summary>
    void Set(string key, string value);
    /// <summary>Reads a setting as a decimal, or fallback if missing/unparseable.</summary>
    decimal GetDecimal(string key, decimal fallback);
    /// <summary>Reads a setting as an int, or fallback if missing/unparseable.</summary>
    int GetInt(string key, int fallback);
}
