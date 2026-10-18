using Microsoft.Data.SqlClient;

namespace WebAPI.Services;

/// <summary>The admin's on/off switches (dbo.featureFlags, admin dashboard "Kenmerkvlae").</summary>
public static class FeatureFlagKeys
{
    /// <summary>On: everyone except admins gets a "maintenance" answer (503); admins can still sign in and work.</summary>
    public const string MaintenanceMode = "Enable_Maintenance_Mode";
    /// <summary>Off: new accounts can't be registered.</summary>
    public const string NewUserRegistrations = "Enable_New_User_Registrations";
    /// <summary>On: PayFast checkout and payment confirmation use PayFast's sandbox; off: the live site.</summary>
    public const string PayFastSandbox = "Enable_PayFast_Sandbox";
}

public interface IFeatureFlags
{
    /// <summary>The flag's current value, or <paramref name="fallback"/> when it doesn't exist or can't be read.</summary>
    Task<bool> IsEnabledAsync(string key, bool fallback);
    /// <summary>Forget the cached values (after an admin toggled a flag).</summary>
    void Invalidate();
}

/// <summary>
/// Reads every flag in one query and keeps them for a few seconds, so checking a flag on each
/// request costs nothing. A toggle on this instance invalidates the cache immediately; other
/// instances (MobileAPI) pick it up within <see cref="Ttl"/>.
/// </summary>
public sealed class FeatureFlagsCache : IFeatureFlags
{
    public static readonly TimeSpan Ttl = TimeSpan.FromSeconds(15);
    private readonly string _connectionString;
    private readonly ILogger<FeatureFlagsCache>? _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private Dictionary<string, bool>? _flags;
    private DateTime _loadedAt = DateTime.MinValue;

    public FeatureFlagsCache(string connectionString, ILogger<FeatureFlagsCache>? logger = null)
    {
        _connectionString = connectionString;
        _logger = logger;
    }

    public async Task<bool> IsEnabledAsync(string key, bool fallback)
    {
        var flags = await LoadAsync();
        return flags != null && flags.TryGetValue(key, out var on) ? on : fallback;
    }

    public void Invalidate() => _loadedAt = DateTime.MinValue;

    private async Task<Dictionary<string, bool>?> LoadAsync()
    {
        if (_flags != null && DateTime.UtcNow - _loadedAt < Ttl) return _flags;
        await _lock.WaitAsync();
        try
        {
            if (_flags != null && DateTime.UtcNow - _loadedAt < Ttl) return _flags;
            var flags = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand("SELECT flagKey, flagIsEnabled FROM dbo.featureFlags", conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                flags[reader.GetString(0)] = reader.GetBoolean(1);
            _flags = flags;
            _loadedAt = DateTime.UtcNow;
            return _flags;
        }
        catch (Exception ex)
        {
            // Keep the last known values (or the callers' fallbacks) rather than failing every request.
            _logger?.LogWarning(ex, "Could not read feature flags");
            _loadedAt = DateTime.UtcNow;   // don't hammer a struggling database
            return _flags;
        }
        finally
        {
            _lock.Release();
        }
    }
}

/// <summary>
/// Maintenance mode: while the flag is on, every request from someone who isn't an admin gets a
/// 503 with an explanation. Sign-in, refresh, sign-out, the session check, public site status
/// and health stay open so admins can still get in and the website can show the maintenance page.
/// Runs after authentication, so an admin's token is recognised.
/// </summary>
public sealed class MaintenanceModeMiddleware
{
    public const string Message = "MikPunt is tydelik in onderhoud. Probeer asseblief later weer.";

    private static readonly string[] OpenPaths =
    {
        "/health", "/api/user/signin", "/api/user/refreshToken", "/api/user/logout", "/api/user/session",
        "/api/settings/public", "/api/settings/status", "/api/admin",
    };

    private readonly RequestDelegate _next;
    public MaintenanceModeMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, IFeatureFlags flags)
    {
        string path = context.Request.Path.Value ?? "";
        bool open = OpenPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));
        if (!open && !context.User.IsInRole("Admin") && await flags.IsEnabledAsync(FeatureFlagKeys.MaintenanceMode, false))
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.Headers.RetryAfter = "300";
            await context.Response.WriteAsJsonAsync(new { success = false, maintenance = true, message = Message });
            return;
        }
        await _next(context);
    }
}
