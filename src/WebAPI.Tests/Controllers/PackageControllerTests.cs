using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Moq;
using WebAPI.Controllers;
using WebAPI.Database;
using WebAPI.Models;
using Xunit;

namespace WebAPI.Tests.Controllers;

public class PackageControllerTests
{
    private readonly Mock<Package_db> _mockDb;
    private readonly Mock<ResourceOwnership_db> _mockOwnership;
    private readonly PackageController _controller;

    private const int CallerUserId = 19;

    public PackageControllerTests()
    {
        _mockDb = new Mock<Package_db>("DummyConnectionString");
        _mockOwnership = new Mock<ResourceOwnership_db>("DummyConnectionString");
        // Packages 1 and 999 belong to the caller; package 5 belongs to someone else.
        _mockOwnership.Setup(o => o.GetPackageOwnerUserIds(1)).ReturnsAsync(new HashSet<int> { CallerUserId });
        _mockOwnership.Setup(o => o.GetPackageOwnerUserIds(999)).ReturnsAsync(new HashSet<int> { CallerUserId });
        _mockOwnership.Setup(o => o.GetPackageOwnerUserIds(5)).ReturnsAsync(new HashSet<int> { 42 });
        _controller = new PackageController(_mockDb.Object, _mockOwnership.Object);
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, CallerUserId.ToString()) }, "Test"))
            }
        };
    }

    [Fact]
    public async Task CreatePacket_WithAnotherOrganiser_IsCreatedForTheCaller()
    {
        var req = new CreatePackageRequest { PTitle = "Kaaper", PPjId = 42 };
        _mockDb.Setup(db => db.CreatePacket(It.IsAny<CreatePackageRequest>())).ReturnsAsync(1);

        await _controller.CreatePacket(req);

        _mockDb.Verify(db => db.CreatePacket(It.Is<CreatePackageRequest>(r => r.PPjId == CallerUserId)), Times.Once);
    }

    [Fact]
    public async Task CreatePacket_ByANonPj_IsForbidden()
    {
        // Package_db.CreatePacket returns 0 (saves nothing) when the caller has no PJ role.
        _mockDb.Setup(db => db.CreatePacket(It.IsAny<CreatePackageRequest>())).ReturnsAsync(0);

        var result = await _controller.CreatePacket(new CreatePackageRequest { PTitle = "Jagter se pakket" });

        Assert.Equal(403, Assert.IsType<ObjectResult>(result).StatusCode);
    }

    [Fact]
    public async Task DeletePacket_WithBookings_SaysItWasDeactivatedNotDeleted()
    {
        _mockDb.Setup(db => db.PackageHasBookings(1)).ReturnsAsync(true);
        _mockDb.Setup(db => db.DeletePacket(1)).ReturnsAsync(true);

        var ok = Assert.IsType<OkObjectResult>(await _controller.DeletePacket(1));

        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        Assert.Contains("\"deactivated\":true", json);
        Assert.Contains("\"deleted\":false", json);
    }

    [Fact]
    public async Task UpdatePacket_ByNonOwner_ReturnsForbid()
    {
        var result = await _controller.UpdatePacket(5, new UpdatePackageRequest { PTitle = "Gekaap" });

        Assert.IsType<ForbidResult>(result);
        _mockDb.Verify(db => db.UpdatePacket(It.IsAny<int>(), It.IsAny<UpdatePackageRequest>()), Times.Never);
    }

    [Fact]
    public async Task DeletePacket_ByNonOwner_ReturnsForbid()
    {
        var result = await _controller.DeletePacket(5);

        Assert.IsType<ForbidResult>(result);
        _mockDb.Verify(db => db.DeletePacket(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task CreatePacket_WhenValid_ReturnsOkWithId()
    {
        // Arrange
        var req = new CreatePackageRequest
        {
            PTitle = "Bosveld Safari",
            PDescription = "5 Dae Jag",
            PStockPrice = 12000,
            PDurationDays = 5,
            PMaxParticipants = 4,
            PFarmId = 2,
            PPjId = 19
        };
        _mockDb.Setup(db => db.CreatePacket(It.IsAny<CreatePackageRequest>())).ReturnsAsync(42);

        // Act
        var result = await _controller.CreatePacket(req);

        // Assert
        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
    }

    [Fact]
    public async Task UpdatePacket_WhenSuccess_ReturnsOk()
    {
        // Arrange
        var req = new UpdatePackageRequest
        {
            PTitle = "Opgedateerde Safari",
            PStockPrice = 15000,
            PFarmId = 3
        };
        _mockDb.Setup(db => db.UpdatePacket(1, req)).ReturnsAsync(true);

        // Act
        var result = await _controller.UpdatePacket(1, req);

        // Assert
        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task UpdatePacket_WhenNotFound_ReturnsNotFound()
    {
        // Arrange
        var req = new UpdatePackageRequest { PTitle = "Onbekend" };
        _mockDb.Setup(db => db.UpdatePacket(999, req)).ReturnsAsync(false);

        // Act
        var result = await _controller.UpdatePacket(999, req);

        // Assert
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetPackets_ReturnsOkWithList()
    {
        // Arrange
        var list = new List<PackageResponse>
        {
            new PackageResponse
            {
                Id = 1,
                Title = "Kalahari Gemsbok Jag",
                OrganiserUserId = 19,
                OrganiserName = "MikPunt Uitruster",
                StockPrice = 25000,
                Status = "Active"
            }
        };
        _mockDb.Setup(db => db.GetPackets()).ReturnsAsync(list);

        // Act
        var result = await _controller.GetPackets();

        // Assert
        var ok = Assert.IsType<OkObjectResult>(result);
        var resList = Assert.IsType<List<PackageResponse>>(ok.Value);
        Assert.Single(resList);
        Assert.Equal("Kalahari Gemsbok Jag", resList[0].Title);
        Assert.Equal(19, resList[0].OrganiserUserId);
    }

    [Fact]
    public async Task GetPacket_WhenFound_ReturnsOk()
    {
        // Arrange
        var pkg = new PackageResponse
        {
            Id = 1,
            Title = "Limpopo Safari",
            OrganiserUserId = 19
        };
        _mockDb.Setup(db => db.GetPacket(1)).ReturnsAsync(pkg);

        // Act
        var result = await _controller.GetPacket(1);

        // Assert
        var ok = Assert.IsType<OkObjectResult>(result);
        var res = Assert.IsType<PackageResponse>(ok.Value);
        Assert.Equal("Limpopo Safari", res.Title);
    }

    [Fact]
    public async Task GetPacket_WhenNotFound_ReturnsNotFound()
    {
        // Arrange
        _mockDb.Setup(db => db.GetPacket(999)).ReturnsAsync((PackageResponse?)null);

        // Act
        var result = await _controller.GetPacket(999);

        // Assert
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetPacketFiltered_ReturnsFilteredList()
    {
        // Arrange
        var filter = new PackageFilterRequest { PPjId = 19, PStatus = "Active" };
        var list = new List<PackageResponse>
        {
            new PackageResponse { Id = 1, Title = "Pakket A", OrganiserUserId = 19, Status = "Active" }
        };
        _mockDb.Setup(db => db.GetPacketFiltered(filter)).ReturnsAsync(list);

        // Act
        var result = await _controller.GetPacketFiltered(filter);

        // Assert
        var ok = Assert.IsType<OkObjectResult>(result);
        var resList = Assert.IsType<List<PackageResponse>>(ok.Value);
        Assert.Single(resList);
    }

    [Fact]
    public async Task DeletePacket_WhenSuccessful_ReturnsOk()
    {
        // Arrange: whether soft-deleted or hard-deleted, DB returns true on success
        _mockDb.Setup(db => db.DeletePacket(1)).ReturnsAsync(true);

        // Act
        var result = await _controller.DeletePacket(1);

        // Assert: a package without bookings is really deleted, and the response says so
        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Contains("\"deleted\":true", System.Text.Json.JsonSerializer.Serialize(ok.Value));
    }

    [Fact]
    public async Task DeletePacket_WhenNotFound_ReturnsNotFound()
    {
        // Arrange
        _mockDb.Setup(db => db.DeletePacket(999)).ReturnsAsync(false);

        // Act
        var result = await _controller.DeletePacket(999);

        // Assert
        Assert.IsType<NotFoundResult>(result);
    }
}
