using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MobileApi.Database;
using MobileApi.Models;
using MobileApi.Models.Auth;
using MobileApi.Options;
using MobileApi.Services;

namespace MobileApi.Controllers;

[ApiController]
[Route("api/auth")]
[AllowAnonymous]
[TransientDbFailureFilter]
public class AuthController : ControllerBase
{
    private readonly Auth_db _authDb;
    private readonly ITokenService _tokenService;
    private readonly JwtOptions _jwtOptions;

    public AuthController(
        Auth_db authDb,
        ITokenService tokenService,
        IOptions<JwtOptions> jwtOptions)
    {
        _authDb = authDb;
        _tokenService = tokenService;
        _jwtOptions = jwtOptions.Value;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return BadRequest(new { message = "Email is required." });
        }

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { message = "Password is required." });
        }

        // The role is read from the database (see Auth_db.SelectRole); the app never chooses it.
        var user = await _authDb.ValidateUserCredentialsAsync(
            request.Email.Trim(),
            request.Password
        );

        if (user is null)
        {
            return Unauthorized(new { message = "Invalid email or password." });
        }

        if (user.UserRoleId <= 0)
        {
            return Unauthorized(new { message = "Hierdie rekening het nog geen rol nie. Voltooi jou registrasie op die MikPunt-webwerf." });
        }

        // Same gates as the WebAPI's sign-in: both log in against the same accounts.
        if (user.IsSuspended)
        {
            return Unauthorized(new { message = "Jou profiel is opgeskort. Kontak asseblief ondersteuning." });
        }

        if (!user.IsEmailVerified)
        {
            return Unauthorized(new
            {
                message = "Jou e-posadres is nog nie geverifieer nie. Verifieer dit eers op die MikPunt-webwerf en teken dan weer in.",
                requiresVerification = true
            });
        }

        var accessToken = _tokenService.CreateAccessToken(user);
        var refreshToken = _tokenService.CreateRefreshToken();
        var refreshTokenExpiry = DateTime.UtcNow.AddDays(_jwtOptions.RefreshTokenDays);

        await _authDb.DeleteRefreshTokensForUserAsync(user.UserId);
        await _authDb.StoreRefreshTokenAsync(
            user.UserId,
            refreshToken,
            refreshTokenExpiry,
            HttpContext.Connection.RemoteIpAddress?.ToString()
        );

        return Ok(CreateResponse(user, accessToken, refreshToken));
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return BadRequest(new { message = "Refresh token is required." });
        }

        var storedToken = await _authDb.GetRefreshTokenAsync(request.RefreshToken.Trim());

        if (storedToken is null)
        {
            return Unauthorized(new { message = "Invalid or expired refresh token." });
        }

        if (string.IsNullOrWhiteSpace(request.AccessToken))
        {
            return Unauthorized(new { message = "Access token is required." });
        }

        try
        {
            var principal = _tokenService.GetPrincipalFromExpiredToken(request.AccessToken);
            var userIdClaim = principal.FindFirstValue(ClaimTypes.NameIdentifier);

            if (!int.TryParse(userIdClaim, out var tokenUserId) || tokenUserId != storedToken.UserId)
            {
                return Unauthorized(new { message = "Refresh token does not match access token." });
            }
        }
        catch
        {
            return Unauthorized(new { message = "Invalid access token." });
        }

        // Re-read the role from the database so a role change on the website reaches the app.
        var user = await _authDb.GetAuthenticatedUserByIdAsync(storedToken.UserId);

        if (user is null)
        {
            return Unauthorized(new { message = "User not found." });
        }

        if (user.IsSuspended)
        {
            await _authDb.DeleteRefreshTokensForUserAsync(user.UserId);
            return Unauthorized(new { message = "Jou profiel is opgeskort. Kontak asseblief ondersteuning." });
        }

        var newAccessToken = _tokenService.CreateAccessToken(user);
        var newRefreshToken = _tokenService.CreateRefreshToken();
        var newRefreshTokenExpiry = DateTime.UtcNow.AddDays(_jwtOptions.RefreshTokenDays);

        await _authDb.DeleteRefreshTokenAsync(storedToken.Token);
        await _authDb.StoreRefreshTokenAsync(
            user.UserId,
            newRefreshToken,
            newRefreshTokenExpiry,
            HttpContext.Connection.RemoteIpAddress?.ToString()
        );

        return Ok(CreateResponse(user, newAccessToken, newRefreshToken));
    }

    private static AuthResponse CreateResponse(AuthenticatedUser user, string accessToken, string refreshToken)
    {
        var mobileRole = MobileAccessService.ParseRole(user.RoleCode);
        return new AuthResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            UserId = user.UserId,
            UserRoleId = user.UserRoleId,
            RoleCode = user.RoleCode,
            Role = MobileAccessService.RoleName(mobileRole),
            Capabilities = MobileAccessService.CapabilitiesFor(mobileRole).OrderBy(x => x).ToArray(),
            Email = user.Email,
            DisplayName = user.DisplayName
        };
    }
}

