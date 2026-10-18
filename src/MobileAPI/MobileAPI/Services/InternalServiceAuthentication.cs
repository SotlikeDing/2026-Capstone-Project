using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using MobileApi.Options;

namespace MobileApi.Services;

/// <summary>
/// Service-to-service authentication for the website. WebAPI has already validated
/// the website user's own JWT; it forwards the request with the shared internal key
/// plus the acting user's id and selected userRoleId. The resulting principal
/// carries the same id claims as a mobile JWT, so MobileAccessService still reads
/// the role from the database (and rejects suspended/unverified users) and the
/// booking participation rules apply unchanged. Only endpoints that opt in via <see cref="BearerOrInternal"/>
/// accept it, and the scheme is disabled entirely while no key is configured.
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
        if (string.IsNullOrWhiteSpace(_internal.Key) || _internal.Key.Length < 32)
            return Task.FromResult(AuthenticateResult.Fail("Internal service authentication is not configured."));
        if (!FixedTimeEquals(presented.ToString(), _internal.Key))
            return Task.FromResult(AuthenticateResult.Fail("Invalid internal service key."));

        if (!int.TryParse(Request.Headers[UserIdHeader], out var userId) || userId <= 0 ||
            !int.TryParse(Request.Headers[UserRoleIdHeader], out var userRoleId) || userRoleId <= 0)
            return Task.FromResult(AuthenticateResult.Fail("Missing acting user."));

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new("userRoleId", userRoleId.ToString()),
            new("authSource", "website")
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }

    private static bool FixedTimeEquals(string presented, string expected)
    {
        var a = SHA256.HashData(Encoding.UTF8.GetBytes(presented));
        var b = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        return CryptographicOperations.FixedTimeEquals(a, b);
    }
}
