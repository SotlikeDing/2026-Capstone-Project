using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;

namespace WebAPI.Services;

/// <summary>Shared secret and peer address for service-to-service calls between WebAPI and MobileAPI.</summary>
public sealed class InternalApiOptions
{
    public const string SectionName = "InternalApi";
    /// <summary>Same value configured on MobileAPI (InternalApi__Key). Empty disables both directions.</summary>
    public string Key { get; set; } = string.Empty;
    /// <summary>Where WebAPI reaches MobileAPI's private booking media endpoints, e.g. http://mobileapi:80.</summary>
    public string MobileApiBaseUrl { get; set; } = string.Empty;

    public bool IsConfigured => Key.Length >= 32;
}

/// <summary>
/// Lets MobileAPI call the few WebAPI endpoints the mobile app needs (post-trip team
/// ratings) on behalf of a user whose mobile JWT it has already validated. Carries
/// only the acting user's id; every endpoint still applies its own participant
/// checks. Endpoints opt in via <see cref="BearerOrInternal"/>, and the scheme stays
/// disabled while no key is configured.
/// </summary>
public sealed class InternalServiceAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "InternalService";
    public const string BearerOrInternal = JwtBearerDefaults.AuthenticationScheme + "," + SchemeName;
    public const string KeyHeader = "X-Internal-Key";
    public const string UserIdHeader = "X-Acting-User-Id";
    public const string UserRoleIdHeader = "X-Acting-User-Role-Id";

    private readonly InternalApiOptions _internal;

    public InternalServiceAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IOptions<InternalApiOptions> internalOptions)
        : base(options, logger, encoder)
    {
        _internal = internalOptions.Value;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(KeyHeader, out var presented))
            return Task.FromResult(AuthenticateResult.NoResult());
        if (!_internal.IsConfigured)
            return Task.FromResult(AuthenticateResult.Fail("Internal service authentication is not configured."));
        if (!FixedTimeEquals(presented.ToString(), _internal.Key))
            return Task.FromResult(AuthenticateResult.Fail("Invalid internal service key."));
        if (!int.TryParse(Request.Headers[UserIdHeader], out var userId) || userId <= 0)
            return Task.FromResult(AuthenticateResult.Fail("Missing acting user."));

        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim("authSource", "mobile")
        }, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }

    public static bool FixedTimeEquals(string presented, string expected)
    {
        var a = SHA256.HashData(Encoding.UTF8.GetBytes(presented));
        var b = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        return CryptographicOperations.FixedTimeEquals(a, b);
    }
}
