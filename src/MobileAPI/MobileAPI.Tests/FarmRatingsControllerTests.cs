using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MobileApi.Controllers;
using MobileApi.Database;
using MobileApi.Models;

namespace MobileAPI.Tests;

public sealed class FarmRatingsControllerTests
{
    [Fact]
    public async Task ZeroStarsIsAValidFarmRating()
    {
        var repository = new FakeFarmRatingsDb();
        var controller = CreateController(repository, 42);

        var result = await controller.SetFarmRating(9, new SetFarmRatingRequest
        {
            Stars = 0,
            Description = "  Opsioneel  "
        });

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(9, repository.BookingId);
        Assert.Equal(42, repository.RaterUserId);
        Assert.Equal(0, repository.Stars);
        Assert.Equal("Opsioneel", repository.Description);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(6)]
    public async Task StarsOutsideZeroToFiveAreRejected(int stars)
    {
        var repository = new FakeFarmRatingsDb();
        var controller = CreateController(repository, 42);

        var result = await controller.SetFarmRating(9, new SetFarmRatingRequest { Stars = stars });

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.False(repository.WasCalled);
    }

    [Fact]
    public async Task RepositoryOwnershipFailureReturnsForbidden()
    {
        var repository = new FakeFarmRatingsDb
        {
            Result = new SaveFarmRatingResult { Status = SaveFarmRatingStatus.Forbidden }
        };
        var controller = CreateController(repository, 42);

        var result = await controller.SetFarmRating(9, new SetFarmRatingRequest { Stars = 5 });

        var response = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, response.StatusCode);
    }

    private static FarmRatingsController CreateController(FarmRatings_db repository, int userId)
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId.ToString())],
            "Test"
        );

        return new FarmRatingsController(repository)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(identity)
                }
            }
        };
    }

    private sealed class FakeFarmRatingsDb() : FarmRatings_db("unused")
    {
        public bool WasCalled { get; private set; }
        public int BookingId { get; private set; }
        public int RaterUserId { get; private set; }
        public int Stars { get; private set; }
        public string? Description { get; private set; }
        public SaveFarmRatingResult Result { get; set; } = new()
        {
            Status = SaveFarmRatingStatus.Success,
            RatingId = 3,
            FarmId = 5
        };

        public override Task<SaveFarmRatingResult> SetFarmRating(
            int bookingId,
            int raterUserId,
            int stars,
            string? description)
        {
            WasCalled = true;
            BookingId = bookingId;
            RaterUserId = raterUserId;
            Stars = stars;
            Description = description;
            return Task.FromResult(Result);
        }
    }
}
