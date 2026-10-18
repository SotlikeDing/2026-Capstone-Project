using System.Collections.Concurrent;
using Microsoft.Data.SqlClient;

namespace WebAPI.Services;

/// <summary>
/// Whether a user is suspended, checked on every authenticated request so a suspension
/// takes effect immediately instead of when the user's (24h) token expires. Answers are
/// cached per user for a minute; AdminController invalidates the entry on a status change.
/// </summary>
public interface IUserStatusCache
{
    Task<bool> IsSuspendedAsync(int userId);
    void Invalidate(int userId);
}

public sealed class UserStatusCache : IUserStatusCache
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);
    private readonly string _connectionString;
    private readonly ConcurrentDictionary<int, (bool Suspended, DateTime CheckedAt)> _cache = new();

    public UserStatusCache(string connectionString) => _connectionString = connectionString;

    public async Task<bool> IsSuspendedAsync(int userId)
    {
        if (_cache.TryGetValue(userId, out var hit) && DateTime.UtcNow - hit.CheckedAt < Ttl)
            return hit.Suspended;
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand("SELECT ISNULL(uIsSuspended, 0) FROM users WHERE userId = @id", conn);
        cmd.Parameters.AddWithValue("@id", userId);
        var suspended = Convert.ToBoolean(await cmd.ExecuteScalarAsync() ?? false);
        _cache[userId] = (suspended, DateTime.UtcNow);
        return suspended;
    }

    public void Invalidate(int userId) => _cache.TryRemove(userId, out _);
}
