using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Moq;
using WebAPI.Controllers;
using WebAPI.Database;
using WebAPI.Models;
using WebAPI.Services;
using Xunit;

namespace WebAPI.Tests.Controllers;

/// <summary>
/// Regression tests for self-service privilege escalation (a user granting themselves the
/// Admin role through register / addRole / selectRole) and for private profile fields
/// leaking through the public getProfile endpoint.
/// </summary>
public class RoleEscalationAndPrivacyTests
{
    private static ControllerContext CallerContext(int? userId, bool isAdmin = false)
    {
        var claims = new List<Claim>();
        if (userId.HasValue) claims.Add(new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString()));
        if (isAdmin) claims.Add(new Claim(ClaimTypes.Role, "Admin"));
        var identity = userId.HasValue ? new ClaimsIdentity(claims, "Test") : new ClaimsIdentity();
        return new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) } };
    }

    private static UserController CreateUserController(Mock<User_db> db, int? callerUserId, bool isAdmin = false)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "UnitTestOnlyJwtSigningKeyThatIsLongEnoughForHS256",
                ["Jwt:Issuer"] = "MikPuntAPI.Tests",
                ["Jwt:Audience"] = "MikPuntClients.Tests"
            })
            .Build();
        var settings = new Mock<ISystemSettingsCache>();
        settings.Setup(s => s.GetInt(It.IsAny<string>(), It.IsAny<int>())).Returns((string _, int fallback) => fallback);
        var controller = new UserController(db.Object, new Mock<IEmailService>().Object, config,
            new Mock<ITokenBlacklistService>().Object, new Mock<ISessionStore>().Object, settings.Object);
        controller.ControllerContext = CallerContext(callerUserId, isAdmin);
        return controller;
    }

    private static RegisterRequest ValidRegistration() => new()
    {
        FirstName = "Jan",
        Surname = "Aanvaller",
        Email = "jan@example.com",
        Password = "Password123!Secure",
        Phone = "0123456789",
        TermsAccepted = true
    };

    [Theory]
    [InlineData("ADMIN")]
    [InlineData("admin")]
    [InlineData("Administrateur")]
    public async Task Register_WithAdminInSelectedRoles_IsRejectedWithoutCreatingTheUser(string roleCode)
    {
        var db = new Mock<User_db>("DummyConnectionString");
        var controller = CreateUserController(db, callerUserId: null);
        var request = ValidRegistration();
        request.SelectedRoles = new List<string> { "Jagter", roleCode };

        var result = await controller.Register(request);

        Assert.IsType<BadRequestObjectResult>(result);
        db.Verify(d => d.RegisterUser(It.IsAny<RegisterRequest>()), Times.Never);
    }

    [Fact]
    public async Task Register_WithAdminAsPrimaryRole_IsRejected()
    {
        var db = new Mock<User_db>("DummyConnectionString");
        var controller = CreateUserController(db, callerUserId: null);
        var request = ValidRegistration();
        request.PrimaryRoleCode = "ADMIN";

        var result = await controller.Register(request);

        Assert.IsType<BadRequestObjectResult>(result);
        db.Verify(d => d.RegisterUser(It.IsAny<RegisterRequest>()), Times.Never);
    }

    [Fact]
    public async Task SelectRole_AdminRoleForSelf_ByNonAdmin_Returns403()
    {
        var db = new Mock<User_db>("DummyConnectionString");
        var controller = CreateUserController(db, callerUserId: 5);

        var result = await controller.SelectRole(new SelectRoleRequest { UserId = 5, RoleCode = "ADMIN" });

        Assert.Equal(403, Assert.IsType<ObjectResult>(result).StatusCode);
        db.Verify(d => d.AssignRoleToUser(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task SelectRole_ForAnotherUser_Returns403()
    {
        var db = new Mock<User_db>("DummyConnectionString");
        var controller = CreateUserController(db, callerUserId: 5);

        var result = await controller.SelectRole(new SelectRoleRequest { UserId = 6, RoleCode = "PH" });

        Assert.Equal(403, Assert.IsType<ObjectResult>(result).StatusCode);
        db.Verify(d => d.AssignRoleToUser(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task AddRole_AdminForSelf_ByNonAdmin_Returns403()
    {
        var db = new Mock<Profile_db>("DummyConnectionString");
        var controller = new ProfileController(db.Object) { ControllerContext = CallerContext(5) };

        var result = await controller.AddRole(5, new AddRoleRequest { RoleCode = "ADMIN" });

        Assert.Equal(403, Assert.IsType<ObjectResult>(result).StatusCode);
        db.Verify(d => d.AddRole(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task AddRole_Admin_ByExistingAdmin_IsAllowed()
    {
        var db = new Mock<Profile_db>("DummyConnectionString");
        db.Setup(d => d.AddRole(6, "ADMIN")).ReturnsAsync(12);
        var controller = new ProfileController(db.Object) { ControllerContext = CallerContext(1, isAdmin: true) };

        var result = await controller.AddRole(6, new AddRoleRequest { RoleCode = "ADMIN" });

        Assert.IsType<OkObjectResult>(result);
    }

    private static ProfileResponse SampleProfile() => new()
    {
        UserId = 3,
        Name = "Sarie",
        Surname = "Marais",
        Email = "sarie@example.com",
        Phone = "0821234567",
        IdPassportNum = "8001015009087"
    };

    [Fact]
    public async Task GetProfile_ForAnonymousVisitor_HidesContactAndIdNumber()
    {
        var db = new Mock<Profile_db>("DummyConnectionString");
        db.Setup(d => d.GetProfile(3)).ReturnsAsync(SampleProfile());
        var controller = new ProfileController(db.Object) { ControllerContext = CallerContext(null) };

        var profile = Assert.IsType<ProfileResponse>(Assert.IsType<OkObjectResult>(await controller.GetProfile(3)).Value);

        Assert.Equal("Sarie", profile.Name);
        Assert.Equal(string.Empty, profile.Email);
        Assert.Null(profile.Phone);
        Assert.Null(profile.IdPassportNum);
    }

    [Fact]
    public async Task GetProfile_ForTheOwner_IncludesContactAndIdNumber()
    {
        var db = new Mock<Profile_db>("DummyConnectionString");
        db.Setup(d => d.GetProfile(3)).ReturnsAsync(SampleProfile());
        var controller = new ProfileController(db.Object) { ControllerContext = CallerContext(3) };

        var profile = Assert.IsType<ProfileResponse>(Assert.IsType<OkObjectResult>(await controller.GetProfile(3)).Value);

        Assert.Equal("sarie@example.com", profile.Email);
        Assert.Equal("0821234567", profile.Phone);
        Assert.Equal("8001015009087", profile.IdPassportNum);
    }

    [Fact]
    public async Task GetHunter_ForSomeoneElsesRole_Returns403()
    {
        var db = new Mock<Profile_db>("DummyConnectionString");
        var ownership = new Mock<ResourceOwnership_db>("DummyConnectionString");
        ownership.Setup(o => o.GetUserIdForUserRole(40)).ReturnsAsync(9);
        var controller = new ProfileController(db.Object, ownership.Object) { ControllerContext = CallerContext(5) };

        var result = await controller.GetHunter(40);

        Assert.Equal(403, Assert.IsType<ObjectResult>(result).StatusCode);
    }

    [Fact]
    public async Task GetHunterBookings_ForAnotherUser_ReturnsForbid()
    {
        var db = new Mock<Bookings_db>("DummyConnectionString");
        var controller = new BookingsController(db.Object) { ControllerContext = CallerContext(5) };

        var result = await controller.GetHunterBookings(6);

        Assert.IsType<ForbidResult>(result);
        db.Verify(d => d.GetHunterBookings(It.IsAny<int>()), Times.Never);
    }
}
