using Microsoft.AspNetCore.Mvc;
using Moq;
using WebAPI.Controllers;
using WebAPI.Database;
using WebAPI.Models;
using Xunit;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace WebAPI.Tests.Controllers;

public class FarmerControllerTests
{
    private readonly Mock<Farmer_db> _mockDb;
    private readonly Mock<Address_db> _mockAddressDb;
    private readonly Mock<ResourceOwnership_db> _mockOwnership;
    private readonly FarmerController _controller;

    private const int CallerUserId = 7;
    private const int CallerFarmerUserRoleId = 10;

    public FarmerControllerTests()
    {
        _mockAddressDb = new Mock<Address_db>("DummyString");
        _mockDb = new Mock<Farmer_db>("DummyConnectionString", _mockAddressDb.Object);
        _mockOwnership = new Mock<ResourceOwnership_db>("DummyConnectionString");
        _mockOwnership.Setup(o => o.GetFarmerUserRoleIdForUser(CallerUserId)).ReturnsAsync(CallerFarmerUserRoleId);
        _mockOwnership.Setup(o => o.GetFarmOwnerUserIds(1)).ReturnsAsync(new HashSet<int> { CallerUserId });
        _mockOwnership.Setup(o => o.GetFarmOwnerUserIds(2)).ReturnsAsync(new HashSet<int> { 99 });
        _controller = new FarmerController(_mockDb.Object, _mockOwnership.Object);

        // Setup a fake signed-in user (the JWT carries the userId, not role-row ids)
        var user = new ClaimsPrincipal(new ClaimsIdentity(new Claim[]
        {
            new Claim(ClaimTypes.NameIdentifier, CallerUserId.ToString())
        }, "mock"));

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };
    }

    [Fact]
    public async Task GetStats_ReturnsOk()
    {
        // Arrange
        var stats = new FarmStatsResponse { TotalFarms = 5 };
        _mockDb.Setup(db => db.GetStats(CallerFarmerUserRoleId)).ReturnsAsync(stats);

        // Act
        var result = await _controller.GetStats();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<FarmStatsResponse>(okResult.Value);
        Assert.Equal(5, response.TotalFarms);
    }

    [Fact]
    public async Task CreateFarm_WithValidRequest_ReturnsOk()
    {
        // Arrange
        var request = new CreateFarmRequest { Name = "Test Farm", Size = 100 };
        _mockDb.Setup(db => db.CreateFarm(CallerUserId, request)).ReturnsAsync(1);

        // Act
        var result = await _controller.CreateFarm(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        // We can't strongly type anonymous type, so we just check it's not null
        Assert.NotNull(okResult.Value);
        
        // Assert property using reflection or dynamic
        var farmIdProperty = okResult.Value.GetType().GetProperty("FarmId");
        Assert.NotNull(farmIdProperty);
        var farmIdValue = farmIdProperty.GetValue(okResult.Value);
        Assert.Equal(1, farmIdValue);
    }

    [Fact]
    public async Task CreateFarm_ByANonFarmer_IsForbidden()
    {
        var request = new CreateFarmRequest { Name = "Test Farm", Size = 100 };
        _mockDb.Setup(db => db.CreateFarm(CallerUserId, request))
               .ThrowsAsync(new UnauthorizedAccessException("Slegs 'n geregistreerde boer kan 'n plaas skep."));

        var result = await _controller.CreateFarm(request);

        Assert.Equal(403, Assert.IsType<ObjectResult>(result).StatusCode);
    }

    [Fact]
    public async Task CreateFarm_ForAnotherUser_IsCreatedForTheCallerInstead()
    {
        // A non-admin cannot create a farm on someone else's account.
        var request = new CreateFarmRequest { Name = "Test Farm", Size = 100, FarmFarmerId = 55 };
        _mockDb.Setup(db => db.CreateFarm(It.IsAny<int>(), request)).ReturnsAsync(1);

        await _controller.CreateFarm(request);

        _mockDb.Verify(db => db.CreateFarm(CallerUserId, request), Times.Once);
        _mockDb.Verify(db => db.CreateFarm(55, It.IsAny<CreateFarmRequest>()), Times.Never);
    }

    [Fact]
    public async Task DeleteFarm_ByOwner_ReturnsOk()
    {
        _mockDb.Setup(db => db.DeleteFarm(1)).ReturnsAsync(true);

        var result = await _controller.DeleteFarm(1);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task DeleteFarm_ByNonOwner_ReturnsForbid()
    {
        var result = await _controller.DeleteFarm(2);

        Assert.IsType<ForbidResult>(result);
        _mockDb.Verify(db => db.DeleteFarm(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task AddAnimal_ToSomeoneElsesFarm_ReturnsForbid()
    {
        var result = await _controller.AddAnimal(new AddAnimalRequest { FarmId = 2, Species = "Koedoe", Count = 3 });

        Assert.IsType<ForbidResult>(result);
        _mockDb.Verify(db => db.AddAnimal(It.IsAny<AddAnimalRequest>()), Times.Never);
    }

    [Fact]
    public async Task GetAllFarms_ReturnsOk()
    {
        // Arrange
        var farms = new List<FarmResponse> 
        { 
            new FarmResponse { Id = 1, Name = "Farm 1" },
            new FarmResponse { Id = 2, Name = "Farm 2" }
        };
        _mockDb.Setup(db => db.GetAllFarms(It.IsAny<string>(), It.IsAny<string>(), 1, 10)).ReturnsAsync(farms);

        // Act
        var result = await _controller.GetAllFarms();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsAssignableFrom<List<FarmResponse>>(okResult.Value);
        Assert.Equal(2, response.Count);
    }

    [Fact]
    public async Task GetFarm_WithValidId_ReturnsOk()
    {
        // Arrange
        var farmId = 1;
        var farm = new FarmResponse { Id = farmId, Name = "Farm 1" };
        _mockDb.Setup(db => db.GetFarm(farmId)).ReturnsAsync(farm);

        // Act
        var result = await _controller.GetFarm(farmId);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<FarmResponse>(okResult.Value);
        Assert.Equal(farmId, response.Id);
    }

    [Fact]
    public async Task GetFarm_WithInvalidId_ReturnsNotFound()
    {
        // Arrange
        var farmId = 99;
        _mockDb.Setup(db => db.GetFarm(farmId)).ReturnsAsync((FarmResponse)null!);

        // Act
        var result = await _controller.GetFarm(farmId);

        // Assert
        Assert.IsType<NotFoundResult>(result);
    }
}

