using Microsoft.AspNetCore.Mvc;
using Moq;
using WebAPI.Controllers;
using WebAPI.Database;
using WebAPI.Models;
using Xunit;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace WebAPI.Tests.Controllers;

public class AdsControllerTests
{
    private readonly Mock<Ads_db> _mockDb;
    private readonly Mock<Address_db> _mockAddressDb;
    private readonly AdsController _controller;

    public AdsControllerTests()
    {
        _mockAddressDb = new Mock<Address_db>("DummyConnectionString");
        _mockDb = new Mock<Ads_db>("DummyConnectionString", _mockAddressDb.Object);
        _controller = new AdsController(_mockDb.Object);

        // Setup a fake authenticated AP user with userId = 42
        var user = new ClaimsPrincipal(new ClaimsIdentity(new Claim[]
        {
            new Claim("userId", "42"),
            new Claim("userRoleId", "105")
        }, "mock"));

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };
    }

    [Fact]
    public async Task CreateAd_WithAuthenticatedUser_LinksAdToSingleApUser()
    {
        // Arrange
        var request = new CreateAdRequest
        {
            Title = "Tactical Hunting Rifle Scope 4-16x50",
            Category = "Gear",
            Price = 100m,
            Description = "Hoë gehalte optika vir jagters."
        };

        _mockDb.Setup(db => db.CreateAd(42, request)).ReturnsAsync(99);

        // Act
        var result = await _controller.CreateAd(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
        
        var idProperty = okResult.Value.GetType().GetProperty("Id");
        Assert.NotNull(idProperty);
        Assert.Equal(99, idProperty.GetValue(okResult.Value));

        _mockDb.Verify(db => db.CreateAd(42, request), Times.Once);
    }

    [Fact]
    public async Task GetAds_WithUserIdFilter_ReturnsOnlyOwnerAds()
    {
        // Arrange
        int userId = 42;
        var ads = new List<AdResponse>
        {
            new AdResponse
            {
                Id = 1,
                ApId = 10,
                UserId = 42,
                AdvertiserName = "Jan Adverteerder",
                Title = "Borg Advertensie 1",
                Status = "Aktief"
            },
            new AdResponse
            {
                Id = 2,
                ApId = 10,
                UserId = 42,
                AdvertiserName = "Jan Adverteerder",
                Title = "Borg Advertensie 2",
                Status = "Aktief"
            }
        };

        _mockDb.Setup(db => db.GetAds(1, 100, null, userId, null)).ReturnsAsync(ads);

        // Act
        var result = await _controller.GetAds(userId: userId);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var responseList = Assert.IsAssignableFrom<List<AdResponse>>(okResult.Value);
        Assert.Equal(2, responseList.Count);
        Assert.All(responseList, ad => Assert.Equal(42, ad.UserId));
    }

    [Fact]
    public async Task UpdateAd_WithUserAuthorization_CallsDbWithUserId()
    {
        // Arrange
        int adId = 99;
        var request = new UpdateAdRequest { Title = "Opgedateerde Titel", Price = 200m };
        _mockDb.Setup(db => db.UpdateAd(adId, request, 42)).ReturnsAsync(true);

        // Act
        var result = await _controller.UpdateAd(adId, request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
        _mockDb.Verify(db => db.UpdateAd(adId, request, 42), Times.Once);
    }

    [Fact]
    public async Task DeleteAd_WithUserAuthorization_CallsDbWithUserId()
    {
        // Arrange
        int adId = 99;
        _mockDb.Setup(db => db.DeleteAd(adId, 42)).ReturnsAsync(true);

        // Act
        var result = await _controller.DeleteAd(adId);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
        _mockDb.Verify(db => db.DeleteAd(adId, 42), Times.Once);
    }

    [Fact]
    public async Task RecordPayment_WithValidPayload_CallsDbWithUserIdAndReturnsSuccess()
    {
        // Arrange
        int adId = 99;
        var request = new RecordPaymentRequest
        {
            PaymentRef = "MP-ADPAY-000099",
            Months = 3,
            Amount = 300m,
            PeriodStartDate = DateTime.UtcNow,
            PeriodEndDate = DateTime.UtcNow.AddMonths(3),
            PaymentMethod = "Simulated Gateway"
        };
        _mockDb.Setup(db => db.RecordPayment(adId, request, 42)).ReturnsAsync(true);

        // Act
        var result = await _controller.RecordPayment(adId, request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
        _mockDb.Verify(db => db.RecordPayment(adId, request, 42), Times.Once);
    }

    [Fact]
    public async Task GetPayments_WithUserIdFilter_ReturnsPaymentHistory()
    {
        // Arrange
        int userId = 42;
        var payments = new List<AdPaymentRecord>
        {
            new AdPaymentRecord
            {
                PaymentId = 1,
                AdId = 99,
                PaymentRef = "MP-ADPAY-000099",
                Amount = 100m,
                Months = 1,
                PaymentDate = DateTime.UtcNow
            }
        };
        _mockDb.Setup(db => db.GetPayments(null, userId)).ReturnsAsync(payments);

        // Act
        var result = await _controller.GetPayments(userId: userId);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var list = Assert.IsAssignableFrom<List<AdPaymentRecord>>(okResult.Value);
        Assert.Single(list);
        Assert.Equal("MP-ADPAY-000099", list[0].PaymentRef);
        _mockDb.Verify(db => db.GetPayments(null, userId), Times.Once);
    }

    [Fact]
    public async Task RecordImpression_WithViewerUserId_PassesViewerToDb()
    {
        // Arrange
        int adId = 99;
        int viewerUserId = 42;
        _mockDb.Setup(db => db.RecordImpression(adId, viewerUserId)).ReturnsAsync(true);

        // Act
        var result = await _controller.RecordImpression(adId, viewerUserId);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
        _mockDb.Verify(db => db.RecordImpression(adId, viewerUserId), Times.Once);
    }

    [Fact]
    public async Task RecordClick_WithViewerUserId_PassesViewerToDb()
    {
        // Arrange
        int adId = 99;
        int viewerUserId = 42;
        _mockDb.Setup(db => db.RecordClick(adId, viewerUserId)).ReturnsAsync(true);

        // Act
        var result = await _controller.RecordClick(adId, viewerUserId);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
        _mockDb.Verify(db => db.RecordClick(adId, viewerUserId), Times.Once);
    }
}
