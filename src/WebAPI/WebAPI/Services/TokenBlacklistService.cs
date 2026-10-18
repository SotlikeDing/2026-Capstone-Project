using System.Collections.Concurrent;

namespace WebAPI.Services;

/// <summary>
/// Shared revoked-token store. In-memory/per-instance is an accepted tradeoff for this
/// capstone deployment; a production multi-instance deployment would back this with a
/// distributed cache (e.g. Redis) instead.
/// </summary>
public class TokenBlacklistService : ITokenBlacklistService
{
    private readonly ConcurrentDictionary<string, DateTime> _revokedTokens = new();

    /// <summary>Adds a token to the blacklist and opportunistically sweeps out any already-expired entries (no background timer - cleanup piggybacks on the next Revoke call).</summary>
    public void Revoke(string token, DateTime expiry)
    {
        _revokedTokens.TryAdd(token, expiry);

        var expiredKeys = _revokedTokens.Where(kvp => kvp.Value < DateTime.UtcNow).Select(kvp => kvp.Key).ToList();
        foreach (var key in expiredKeys)
        {
            _revokedTokens.TryRemove(key, out _);
        }
    }

    public bool IsRevoked(string token) => _revokedTokens.ContainsKey(token);
}
