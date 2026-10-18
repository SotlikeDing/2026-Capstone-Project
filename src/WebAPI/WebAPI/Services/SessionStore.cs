using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace WebAPI.Services;

/// <summary>
/// Shared refresh-token store, also used to report a real "active sessions" count on
/// the admin health panel instead of a hardcoded number. In-memory/per-instance, same
/// tradeoff as <see cref="TokenBlacklistService"/>.
/// </summary>
public class SessionStore : ISessionStore
{
    private readonly ConcurrentDictionary<string, (string RefreshToken, DateTime Expiry)> _refreshTokens = new();
    private readonly ISystemSettingsCache _settingsCache;

    public SessionStore(ISystemSettingsCache settingsCache)
    {
        _settingsCache = settingsCache;
    }

    /// <summary>Issues a cryptographically random 256-bit refresh token, replacing any existing one for this email (single active refresh token per account).</summary>
    public string CreateRefreshToken(string email)
    {
        byte[] bytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        string token = Convert.ToBase64String(bytes);
        string key = email.ToLowerInvariant();
        int expiryDays = _settingsCache.GetInt("RefreshTokenExpiryDays", 7);
        _refreshTokens[key] = (token, DateTime.UtcNow.AddDays(expiryDays));
        return token;
    }

    /// <summary>Whether refreshToken matches the current, unexpired token on file for this email.</summary>
    public bool TryValidateRefreshToken(string email, string refreshToken)
    {
        string key = email.ToLowerInvariant();
        if (_refreshTokens.TryGetValue(key, out var info))
        {
            return info.RefreshToken == refreshToken && info.Expiry > DateTime.UtcNow;
        }
        return false;
    }

    /// <summary>Count of not-yet-expired refresh tokens - a rough proxy for "logged-in users", not a precise concurrent-session count.</summary>
    public int CountActiveSessions()
    {
        var now = DateTime.UtcNow;
        return _refreshTokens.Count(kvp => kvp.Value.Expiry > now);
    }
}
