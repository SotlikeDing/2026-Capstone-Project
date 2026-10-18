using System.Security.Claims;
using MobileApi.Models.Auth;

namespace MobileApi.Services;

public interface ITokenService
{
    string CreateAccessToken(AuthenticatedUser user);
    string CreateRefreshToken();
    ClaimsPrincipal GetPrincipalFromExpiredToken(string token);
}
