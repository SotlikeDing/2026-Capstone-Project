using Microsoft.AspNetCore.Mvc;
using Moq;
using WebAPI.Controllers;
using WebAPI.Database;
using WebAPI.Models;
using WebAPI.Services;
using Xunit;

using Microsoft.Extensions.Configuration;

namespace WebAPI.Tests.Controllers;

public class UserControllerTests
{
    private readonly Mock<User_db> _mockDb;
    private readonly Mock<IEmailService> _mockEmailService;
    private readonly IConfiguration _configuration;
    private readonly Mock<ITokenBlacklistService> _mockTokenBlacklist;
    private readonly Mock<ISessionStore> _mockSessionStore;
    private readonly Mock<ISystemSettingsCache> _mockSettingsCache;
    private readonly UserController _controller;

    public UserControllerTests()
    {
        _mockDb = new Mock<User_db>("DummyConnectionString");
        _mockEmailService = new Mock<IEmailService>();
        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "UnitTestOnlyJwtSigningKeyThatIsLongEnoughForHS256",
                ["Jwt:Issuer"] = "MikPuntAPI.Tests",
                ["Jwt:Audience"] = "MikPuntClients.Tests"
            })
            .Build();
        _mockTokenBlacklist = new Mock<ITokenBlacklistService>();
        _mockSessionStore = new Mock<ISessionStore>();
        _mockSettingsCache = new Mock<ISystemSettingsCache>();
        _mockSettingsCache.Setup(s => s.GetInt(It.IsAny<string>(), It.IsAny<int>())).Returns((string _, int fallback) => fallback);
        _mockSettingsCache.Setup(s => s.GetDecimal(It.IsAny<string>(), It.IsAny<decimal>())).Returns((string _, decimal fallback) => fallback);
        _controller = new UserController(_mockDb.Object, _mockEmailService.Object, _configuration, _mockTokenBlacklist.Object, _mockSessionStore.Object, _mockSettingsCache.Object);
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext()
        };
    }

    [Fact]
    public async Task SignIn_WithValidCredentials_ReturnsOk()
    {
        // Arrange
        var request = new LoginRequest { Email = "test@example.com", Password = "Password123!" };
        var user = new User 
        { 
            UserId = 1, 
            Email = "test@example.com", 
            IsEmailVerified = true, 
            IsSuspended = false,
            Roles = new List<UserRole> { new UserRole { RoleCode = "HUNTER" } }
        };

        _mockDb.Setup(db => db.AuthenticateUser(request)).ReturnsAsync(user);

        // Act
        var result = await _controller.SignIn(request);

        // Assert
        var objResult = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(200, objResult.StatusCode);
        var response = Assert.IsType<SignInResponse>(objResult.Value);
        Assert.True(response.Success);
        Assert.Equal(1, response.UserId);
        Assert.NotEmpty(response.AccessToken);
    }

    [Fact]
    public async Task SignIn_WithUnverifiedEmail_IsRefusedAndAsksForVerification()
    {
        var request = new LoginRequest { Email = "new@example.com", Password = "Password123!" };
        _mockDb.Setup(db => db.AuthenticateUser(request)).ReturnsAsync(new User
        {
            UserId = 2,
            Email = "new@example.com",
            IsEmailVerified = false,
            Roles = new List<UserRole> { new UserRole { RoleCode = "HUNTER" } }
        });

        var result = await _controller.SignIn(request);

        var objResult = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(401, objResult.StatusCode);
        Assert.Contains("requiresVerification = True", objResult.Value!.ToString());
    }

    [Fact]
    public async Task SignIn_WithInvalidCredentials_ReturnsUnauthorized()
    {
        // Arrange
        var request = new LoginRequest { Email = "test@example.com", Password = "WrongPassword!" };
        _mockDb.Setup(db => db.AuthenticateUser(request)).ReturnsAsync((User)null!);

        // Act
        var result = await _controller.SignIn(request);

        // Assert
        var objResult = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(401, objResult.StatusCode);
        Assert.NotNull(objResult.Value);
    }

    [Fact]
    public async Task Register_WithValidData_ReturnsOk()
    {
        // Arrange
        var request = new RegisterRequest 
        { 
            FirstName = "John", 
            Surname = "Doe",
            Email = "john@example.com", 
            Password = "Password123!Secure",
            Phone = "0123456789",
            TermsAccepted = true
        };

        _mockDb.Setup(db => db.UserExists(request.Email)).ReturnsAsync(false);
        _mockDb.Setup(db => db.RegisterUser(request)).ReturnsAsync(true);
        _mockDb.Setup(db => db.GetUserByEmail(request.Email)).ReturnsAsync(new User { UserId = 1, Email = request.Email });
        
        // Act
        var result = await _controller.Register(request);

        // Assert
        var okResult = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(200, okResult.StatusCode);
        var response = Assert.IsType<RegisterResponse>(okResult.Value);
        Assert.True(response.Success);
    }

    [Fact]
    public async Task Register_WithExistingEmail_ReturnsBadRequest()
    {
        // Arrange
        var request = new RegisterRequest 
        { 
            FirstName = "John", 
            Surname = "Doe",
            Email = "john@example.com", 
            Password = "Password123!Secure",
            Phone = "0123456789",
            TermsAccepted = true
        };

        _mockDb.Setup(db => db.UserExists(request.Email)).ReturnsAsync(true);

        // Act
        var result = await _controller.Register(request);

        // Assert
        var badRequestResult = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(400, badRequestResult.StatusCode);
        var response = Assert.IsType<RegisterResponse>(badRequestResult.Value);
        Assert.False(response.Success);
        Assert.Contains("bestaan reeds", response.Message);
    }
}

