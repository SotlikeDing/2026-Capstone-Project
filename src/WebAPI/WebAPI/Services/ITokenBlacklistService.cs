namespace WebAPI.Services;

/// <summary>Revocation list for JWTs logged out before their natural expiry - see TokenBlacklistService for the in-memory implementation.</summary>
public interface ITokenBlacklistService
{
    /// <summary>Blacklists a token until its own expiry (no point keeping it revoked past the point it would've stopped working anyway).</summary>
    void Revoke(string token, DateTime expiry);
    /// <summary>Whether a token has been revoked.</summary>
    bool IsRevoked(string token);
}
