using Microsoft.AspNetCore.Mvc;
using Moq;
using WebAPI.Controllers;
using WebAPI.Database;
using WebAPI.Models;
using Xunit;

namespace WebAPI.Tests.Controllers;

public class ProfileControllerTests
{
    private readonly Mock<Profile_db> _mockDb;
    private readonly ProfileController _controller;

    public ProfileControllerTests()
    {
        _mockDb = new Mock<Profile_db>("DummyConnectionString");
        _controller = new ProfileController(_mockDb.Object);

        // Default caller: signed in as user 1 (the userId most tests act on). Tests that
        // need a different caller replace ControllerContext themselves.
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
            {
                User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(new[]
                {
                    new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, "1")
                }, "Test"))
            }
        };
    }

    [Fact]
    public async Task UpdateProfile_WhenAnonymous_ReturnsForbidden403()
    {
        _controller.ControllerContext = new ControllerContext { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() };

        var result = await _controller.UpdateProfile(1, new UpdateProfileRequest { Name = "Hacker" });

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, objectResult.StatusCode);
        _mockDb.Verify(db => db.UpdateProfile(It.IsAny<int>(), It.IsAny<UpdateProfileRequest>()), Times.Never);
    }

    [Fact]
    public async Task GetProfile_WithValidUserId_ReturnsOk()
    {
        // Arrange
        var userId = 1;
        var profile = new ProfileResponse { UserId = userId, Name = "John", Surname = "Doe" };
        _mockDb.Setup(db => db.GetProfile(userId)).ReturnsAsync(profile);

        // Act
        var result = await _controller.GetProfile(userId);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<ProfileResponse>(okResult.Value);
        Assert.Equal(userId, response.UserId);
        Assert.Equal("John", response.Name);
    }

    [Fact]
    public async Task GetProfile_WithInvalidUserId_ReturnsNotFound()
    {
        // Arrange
        var userId = 99;
        _mockDb.Setup(db => db.GetProfile(userId)).ReturnsAsync((ProfileResponse)null!);

        // Act
        var result = await _controller.GetProfile(userId);

        // Assert
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task UpdateProfile_WithValidData_ReturnsOk()
    {
        // Arrange
        var userId = 1;
        var request = new UpdateProfileRequest { Name = "John Updated" };
        _mockDb.Setup(db => db.UpdateProfile(userId, request)).ReturnsAsync(true);

        // Act
        var result = await _controller.UpdateProfile(userId, request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public async Task AddRole_WithValidRole_ReturnsOk()
    {
        // Arrange
        var userId = 1;
        var request = new AddRoleRequest { RoleCode = "HUNTER" };
        _mockDb.Setup(db => db.AddRole(userId, request.RoleCode)).ReturnsAsync(5);

        // Act
        var result = await _controller.AddRole(userId, request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public async Task AddRole_WithDuplicateRole_ReturnsBadRequest()
    {
        // Arrange
        var userId = 1;
        var request = new AddRoleRequest { RoleCode = "HUNTER" };
        _mockDb.Setup(db => db.AddRole(userId, request.RoleCode)).ReturnsAsync(-1);

        // Act
        var result = await _controller.AddRole(userId, request);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(badRequestResult.Value);
    }

    [Fact]
    public async Task RegisterAp_WhenAuthorized_ReturnsOk()
    {
        // Arrange
        var userId = 55;
        var user = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(new[]
        {
            new System.Security.Claims.Claim("userId", "55")
        }, "mock"));

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext { User = user }
        };

        _mockDb.Setup(db => db.AddRole(userId, "AP")).ReturnsAsync(10);
        _mockDb.Setup(db => db.RegisterAp(10)).ReturnsAsync(true);

        // Act
        var result = await _controller.RegisterAp(userId);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public async Task RegisterAp_WhenUnauthorizedCaller_ReturnsForbidden403()
    {
        // Arrange
        var targetUserId = 55;
        var attacker = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(new[]
        {
            new System.Security.Claims.Claim("userId", "99") // different user
        }, "mock"));

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext { User = attacker }
        };

        // Act
        var result = await _controller.RegisterAp(targetUserId);

        // Assert
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, objectResult.StatusCode);
    }

    [Fact]
    public async Task RegisterAp_WhenAdminCallerForOtherUser_ReturnsOk()
    {
        // Arrange
        var targetUserId = 55;
        var admin = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(new[]
        {
            new System.Security.Claims.Claim("userId", "1"),
            new System.Security.Claims.Claim("role", "Admin")
        }, "mock"));

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext { User = admin }
        };

        _mockDb.Setup(db => db.AddRole(targetUserId, "AP")).ReturnsAsync(11);
        _mockDb.Setup(db => db.RegisterAp(11)).ReturnsAsync(true);

        // Act
        var result = await _controller.RegisterAp(targetUserId);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public async Task DeleteRoleByCode_WhenUnauthorizedCaller_ReturnsForbidden403()
    {
        // Arrange
        var targetUserId = 55;
        var attacker = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(new[]
        {
            new System.Security.Claims.Claim("userId", "99")
        }, "mock"));

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext { User = attacker }
        };

        // Act
        var result = await _controller.DeleteRoleByCode(targetUserId, "AP");

        // Assert
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, objectResult.StatusCode);
    }
}

