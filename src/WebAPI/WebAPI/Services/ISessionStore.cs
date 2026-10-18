namespace WebAPI.Services;

/// <summary>Refresh-token issuance/validation and active-session counting - see SessionStore for the in-memory implementation.</summary>
public interface ISessionStore
{
    /// <summary>Issues a new refresh token for an email, replacing any prior one.</summary>
    string CreateRefreshToken(string email);
    /// <summary>Whether refreshToken is the currently-valid one for this email.</summary>
    bool TryValidateRefreshToken(string email, string refreshToken);
    /// <summary>Count of live sessions, for the admin health dashboard.</summary>
    int CountActiveSessions();
}
