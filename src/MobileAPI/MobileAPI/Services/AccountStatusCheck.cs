using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;

namespace MobileApi.Services;

/// <summary>
/// The account and site switches the admin sets on the website (shared database): whether a user
/// is suspended, and whether maintenance mode is on. Each answer is cached briefly so checking on
/// every request stays cheap.
/// </summary>
public sealed class AccountStatusCheck
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(15);
    private readonly string _connectionString;
    private readonly IMemoryCache _cache;

    public AccountStatusCheck(string connectionString, IMemoryCache cache)
    {
        _connectionString = connectionString;
        _cache = cache;
    }

    public async Task<bool> IsSuspendedAsync(int userId)
    {
        if (_cache.TryGetValue($"suspended|{userId}", out bool cached)) return cached;
        bool suspended = false;
        try
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand("SELECT CAST(ISNULL(uIsSuspended, 0) AS BIT) FROM users WHERE userId = @id", conn);
            cmd.Parameters.AddWithValue("@id", userId);
            suspended = await cmd.ExecuteScalarAsync() is true;
        }
        catch (SqlException)
        {
            return false;   // don't lock everyone out when the database hiccups; sign-in still checks
        }
        _cache.Set($"suspended|{userId}", suspended, Ttl);
        return suspended;
    }

    public async Task<bool> IsMaintenanceModeAsync()
    {
        if (_cache.TryGetValue("maintenance", out bool cached)) return cached;
        bool on = false;
        try
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand("SELECT flagIsEnabled FROM featureFlags WHERE flagKey = 'Enable_Maintenance_Mode'", conn);
            on = await cmd.ExecuteScalarAsync() is true;
        }
        catch (SqlException)
        {
            return false;
        }
        _cache.Set("maintenance", on, Ttl);
        return on;
    }
}

/// <summary>Maintenance mode for the app: everyone but admins gets a 503 with an explanation; sign-in and health stay open.</summary>
public sealed class MaintenanceModeMiddleware
{
    private readonly RequestDelegate _next;
    public MaintenanceModeMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, AccountStatusCheck status)
    {
        string path = context.Request.Path.Value ?? "";
        bool open = path.StartsWith("/api/auth", StringComparison.OrdinalIgnoreCase) || path.StartsWith("/health", StringComparison.OrdinalIgnoreCase);
        bool admin = context.User.IsInRole("ADMIN") || context.User.IsInRole("Admin");
        if (!open && !admin && await status.IsMaintenanceModeAsync())
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.Headers.RetryAfter = "300";
            await context.Response.WriteAsJsonAsync(new { success = false, maintenance = true, message = "MikPunt is tydelik in onderhoud. Probeer asseblief later weer." });
            return;
        }
        await _next(context);
    }
}
