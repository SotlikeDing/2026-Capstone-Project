using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace WebAPI.Tests;

/// <summary>
/// Proves the [Authorize(Roles = "Admin")] + JWT bearer pipeline actually works end to
/// end (the admin auth bypass this covers used to be a dead check that always returned
/// true). Hits the DB-free /api/admin/ping endpoint so it doesn't need a live SQL Server.
/// </summary>
public class AuthIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AuthIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private string CreateToken(string roleCode)
    {
        var config = _factory.Services.GetRequiredService<IConfiguration>();
        var key = Encoding.UTF8.GetBytes(config["Jwt:Key"]!);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "1"),
            new(ClaimTypes.Email, "admin@example.com"),
            new(ClaimTypes.Name, "Test Admin"),
            new(ClaimTypes.Role, roleCode)
        };

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = config["Jwt:Issuer"],
            Audience = config["Jwt:Audience"],
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };

        var handler = new JwtSecurityTokenHandler();
        return handler.WriteToken(handler.CreateToken(tokenDescriptor));
    }

    [Fact]
    public async Task Ping_WithoutToken_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/admin/ping");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Ping_WithNonAdminRole_ReturnsForbidden()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken("Jagter"));

        var response = await client.GetAsync("/api/admin/ping");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Ping_WithAdminRole_ReturnsOk()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken("Admin"));

        var response = await client.GetAsync("/api/admin/ping");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// The DB stores role codes as "ADMIN" (see db/ToetsData.sql, db/Complete_create.sql),
    /// and UserController.GenerateJwtToken puts that exact casing into the role claim.
    /// [Authorize(Roles = "Admin")] alone is an ordinal, case-sensitive match, so without
    /// the claim-normalization in Program.cs's OnTokenValidated this would 403 for every
    /// real user in the database even though they genuinely hold the Admin role.
    /// </summary>
    [Fact]
    public async Task Ping_WithUppercaseAdminRoleAsStoredInDb_ReturnsOk()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken("ADMIN"));

        var response = await client.GetAsync("/api/admin/ping");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// POST api/rating/setRoleRating used to have no [Authorize] at all - anyone could
    /// forge a rating for anyone, as anyone, with a bare unauthenticated request. This
    /// proves the pipeline actually rejects it before the request ever reaches the
    /// controller (and therefore never touches the DB, so no live SQL Server needed here).
    /// </summary>
    [Fact]
    public async Task SetRoleRating_WithoutToken_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();
        var body = System.Net.Http.Json.JsonContent.Create(new { bookingId = 1, raterUserId = 1, rateeUserId = 2, rateeRoleCode = "PH", stars = 5 });

        var response = await client.PostAsync("/api/rating/setRoleRating", body);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// These controllers used to have no [Authorize] anywhere, so anyone could create,
    /// edit or delete packages, farms, profiles, weapons, permits, etc. with a bare
    /// request. The pipeline now rejects them before they reach the controller (no DB needed).
    /// </summary>
    [Theory]
    [InlineData("DELETE", "/api/packets/deletePacket/1")]
    [InlineData("POST", "/api/packets/createPacket")]
    [InlineData("POST", "/api/farmer/create")]
    [InlineData("DELETE", "/api/farmer/delete/1")]
    [InlineData("DELETE", "/api/profile/deleteUser/1")]
    [InlineData("PUT", "/api/profile/updateProfile/1")]
    [InlineData("POST", "/api/profile/addRole/1")]
    [InlineData("DELETE", "/api/ads/deleteAd/1")]
    [InlineData("GET", "/api/ads/getPayments")]
    [InlineData("GET", "/api/weapon/getWeapons")]
    [InlineData("GET", "/api/permits/1")]
    [InlineData("POST", "/api/huntingpermit/setApproval/1")]
    [InlineData("DELETE", "/api/phpricing/delete/1")]
    [InlineData("DELETE", "/api/vgpackages/delete/1")]
    [InlineData("GET", "/api/bookings/getBookingRequest")]
    [InlineData("DELETE", "/api/team/deleteTeamMember/1")]
    [InlineData("POST", "/api/user/selectRole")]
    [InlineData("POST", "/chathub/negotiate?negotiateVersion=1")]
    public async Task FormerlyOpenEndpoints_WithoutToken_ReturnUnauthorized(string method, string url)
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (method is "POST" or "PUT") request.Content = System.Net.Http.Json.JsonContent.Create(new { });

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// [AllowAnonymous] endpoints get past the auth pipeline without a token. Asserts only
    /// "not 401": under TestServer a JSON body currently fails to serialize (the test
    /// project's System.Text.Json is newer than ASP.NET 8's TestServer supports).
    /// </summary>
    [Fact]
    public async Task PublicEndpoint_WithoutToken_IsNotRejectedByAuth()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/user/checkToken?token=not-a-revoked-token");

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>Approving/listing all SAPS permit applications is Admin-only, not just "signed in".</summary>
    [Fact]
    public async Task HuntingPermitApplications_WithNonAdminRole_ReturnsForbidden()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken("HUNTER"));

        var response = await client.GetAsync("/api/huntingpermit/getApplication");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
