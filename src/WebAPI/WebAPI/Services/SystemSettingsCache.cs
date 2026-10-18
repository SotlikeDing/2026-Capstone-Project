using System.Collections.Concurrent;
using System.Globalization;
using WebAPI.Database;

namespace WebAPI.Services;

/// <summary>
/// In-memory read cache over the systemSettings table. Loaded once at startup
/// (Program.cs) and updated in-place by AdminController right after every successful
/// write, so admin changes to pricing/operational constants take effect immediately -
/// no app restart, no polling delay.
/// </summary>
public class SystemSettingsCache : ISystemSettingsCache
{
    private readonly ConcurrentDictionary<string, string> _values = new();

    public async Task RefreshAsync(SystemSettings_db settingsDb)
    {
        var all = await settingsDb.GetAllAsync();
        foreach (var setting in all)
        {
            _values[setting.Key] = setting.Value;
        }
    }

    public void Set(string key, string value) => _values[key] = value;

    public decimal GetDecimal(string key, decimal fallback)
    {
        if (_values.TryGetValue(key, out var raw) && decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }
        return fallback;
    }

    public int GetInt(string key, int fallback)
    {
        if (_values.TryGetValue(key, out var raw) && int.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }
        return fallback;
    }
}
