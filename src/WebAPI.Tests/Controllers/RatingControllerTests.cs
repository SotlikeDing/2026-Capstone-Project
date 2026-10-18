using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using WebAPI.Controllers;
using WebAPI.Database;
using WebAPI.Models;
using Xunit;

namespace WebAPI.Tests.Controllers;

public class RatingControllerTests
{
    private readonly Mock<Rating_db> _mockRatingDb;
    private readonly Mock<Bookings_db> _mockBookingsDb;
    private readonly RatingController _controller;

    public RatingControllerTests()
    {
        _mockRatingDb = new Mock<Rating_db>("DummyConnectionString");
        _mockBookingsDb = new Mock<Bookings_db>("DummyConnectionString");
        _controller = new RatingController(_mockRatingDb.Object, _mockBookingsDb.Object);
        SetCaller(1);
    }

    private void SetCaller(int userId)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString())
        }, "TestAuth"));
        _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
    }

    private static SetRatingRequest ValidRequest() => new()
    {
        BookingId = 3,
        RaterUserId = 1,
        RateeUserId = 2,
        RateeRoleCode = "PH",
        Stars = 5,
        Comment = "Great trip"
    };

    private void SetupCompletedBookingWithParticipants()
    {
        _mockBookingsDb.Setup(db => db.GetBookingParticipants(3)).ReturnsAsync(new BookingParticipantsDto
        {
            BookingExists = true,
            Status = "Completed",
            HunterUserId = 1,
            PhUserId = 2,
            VgUserId = null
        });
    }

    [Fact]
    public async Task SetRoleRating_WithValidRequest_ReturnsOk()
    {
        SetupCompletedBookingWithParticipants();
        var request = ValidRequest();
        _mockRatingDb.Setup(db => db.SetRoleRating(request)).ReturnsAsync(42);

        var result = await _controller.SetRoleRating(request);

        Assert.IsType<OkObjectResult>(result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public async Task SetRoleRating_WithStarsOutOfRange_ReturnsBadRequest(int stars)
    {
        var request = ValidRequest();
        request.Stars = stars;

        var result = await _controller.SetRoleRating(request);

        Assert.IsType<BadRequestObjectResult>(result);
        _mockRatingDb.Verify(db => db.SetRoleRating(It.IsAny<SetRatingRequest>()), Times.Never);
    }

    [Fact]
    public async Task SetRoleRating_WhenRatingSelf_ReturnsBadRequest()
    {
        var request = ValidRequest();
        request.RateeUserId = request.RaterUserId;

        var result = await _controller.SetRoleRating(request);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task SetRoleRating_WithInvalidRoleCode_ReturnsBadRequest()
    {
        var request = ValidRequest();
        request.RateeRoleCode = "Boer";

        var result = await _controller.SetRoleRating(request);

        Assert.IsType<BadRequestObjectResult>(result);
        _mockBookingsDb.Verify(db => db.GetBookingParticipants(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task SetRoleRating_WithInvalidBookingOrUserIds_ReturnsBadRequest()
    {
        var request = ValidRequest();
        request.BookingId = 0;

        var result = await _controller.SetRoleRating(request);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task SetRoleRating_WhenCallerIsNotTheRater_ReturnsForbidden()
    {
        SetCaller(999); // authenticated as someone other than RaterUserId (1)
        var request = ValidRequest();

        var result = await _controller.SetRoleRating(request);

        var statusResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, statusResult.StatusCode);
        _mockRatingDb.Verify(db => db.SetRoleRating(It.IsAny<SetRatingRequest>()), Times.Never);
    }

    [Fact]
    public async Task SetRoleRating_WhenBookingDoesNotExist_ReturnsNotFound()
    {
        _mockBookingsDb.Setup(db => db.GetBookingParticipants(3)).ReturnsAsync(new BookingParticipantsDto { BookingExists = false });

        var result = await _controller.SetRoleRating(ValidRequest());

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task SetRoleRating_WhenConfirmedTripHasEnded_ReturnsOk()
    {
        // Nothing flips a finished trip to Completed automatically, so a paid trip
        // whose last day has passed must be rateable.
        _mockBookingsDb.Setup(db => db.GetBookingParticipants(3)).ReturnsAsync(new BookingParticipantsDto
        {
            BookingExists = true,
            Status = "Paid",
            EndDate = DateTime.UtcNow.Date.AddDays(-1),
            HunterUserId = 1,
            PhUserId = 2
        });
        var request = ValidRequest();
        _mockRatingDb.Setup(db => db.SetRoleRating(request)).ReturnsAsync(42);

        var result = await _controller.SetRoleRating(request);

        Assert.IsType<OkObjectResult>(result);
    }

    [Theory]
    [InlineData("Paid", 0)]       // ends today - not over yet
    [InlineData("Pending", -3)]   // never confirmed
    [InlineData("Cancelled", -3)]
    public async Task SetRoleRating_WhenTripNotOverOrNotConfirmed_ReturnsBadRequest(string status, int endOffsetDays)
    {
        _mockBookingsDb.Setup(db => db.GetBookingParticipants(3)).ReturnsAsync(new BookingParticipantsDto
        {
            BookingExists = true,
            Status = status,
            EndDate = DateTime.UtcNow.Date.AddDays(endOffsetDays),
            HunterUserId = 1,
            PhUserId = 2
        });

        var result = await _controller.SetRoleRating(ValidRequest());

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task GetBookingTeam_WhenCallerWasNotOnBooking_ReturnsForbidden()
    {
        SetupCompletedBookingWithParticipants();
        SetCaller(99);

        var result = await _controller.GetBookingTeam(3);

        Assert.Equal(403, Assert.IsType<ObjectResult>(result).StatusCode);
    }

    [Fact]
    public async Task GetBookingTeam_ListsTheOtherParticipantsWithCallersRatings()
    {
        _mockBookingsDb.Setup(db => db.GetBookingParticipants(3)).ReturnsAsync(new BookingParticipantsDto
        {
            BookingExists = true,
            Status = "Completed",
            HunterUserId = 1,
            HunterName = "Jan Jagter",
            PhUserId = 2,
            PhName = "Piet PH",
            VgUserId = 5,
            VgName = "Vera VG"
        });
        _mockRatingDb.Setup(db => db.GetOwnRatingsForBooking(3, 1)).ReturnsAsync(new List<OwnRatingDto>
        {
            new() { RateeUserId = 2, Stars = 4, Comment = "Goeie gids" }
        });

        var result = await _controller.GetBookingTeam(3);

        var team = Assert.IsType<BookingTeamRatingDto>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.True(team.CanRate);
        Assert.Equal(1, team.CallerUserId);
        Assert.Equal(new[] { 2, 5 }, team.Members.Select(m => m.UserId));
        var ph = team.Members.Single(m => m.UserId == 2);
        Assert.Equal("PH", ph.RoleCode);
        Assert.Equal(4, ph.MyStars);
        Assert.Null(team.Members.Single(m => m.UserId == 5).MyStars);
    }

    [Fact]
    public async Task SetRoleRating_WhenBookingNotCompleted_ReturnsBadRequest()
    {
        _mockBookingsDb.Setup(db => db.GetBookingParticipants(3)).ReturnsAsync(new BookingParticipantsDto
        {
            BookingExists = true,
            Status = "Paid",
            HunterUserId = 1,
            PhUserId = 2
        });

        var result = await _controller.SetRoleRating(ValidRequest());

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task SetRoleRating_WhenRaterWasNotOnBooking_ReturnsForbidden()
    {
        _mockBookingsDb.Setup(db => db.GetBookingParticipants(3)).ReturnsAsync(new BookingParticipantsDto
        {
            BookingExists = true,
            Status = "Completed",
            HunterUserId = 55, // rater (1) was not the hunter, PH or VG on this booking
            PhUserId = 2,
            VgUserId = null
        });

        var result = await _controller.SetRoleRating(ValidRequest());

        var statusResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, statusResult.StatusCode);
    }

    [Fact]
    public async Task SetRoleRating_WhenRateeDidNotHoldStatedRole_ReturnsBadRequest()
    {
        _mockBookingsDb.Setup(db => db.GetBookingParticipants(3)).ReturnsAsync(new BookingParticipantsDto
        {
            BookingExists = true,
            Status = "Completed",
            HunterUserId = 1,
            PhUserId = 99, // rating claims RateeUserId=2 was the PH, but 99 actually was
            VgUserId = null
        });

        var result = await _controller.SetRoleRating(ValidRequest());

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task GetProfileStats_CombinesRatingSummaryAndCompletedTrips()
    {
        int userId = 2;
        _mockRatingDb.Setup(db => db.GetRatingSummary(userId, "PH")).ReturnsAsync(new RatingSummaryDto { AverageStars = 4.5m, RatingCount = 2 });
        _mockBookingsDb.Setup(db => db.GetCompletedTripCount(userId)).ReturnsAsync(3);

        var result = await _controller.GetProfileStats(userId, "PH");

        var okResult = Assert.IsType<OkObjectResult>(result);
        var stats = Assert.IsType<RatingProfileStatsDto>(okResult.Value);
        Assert.Equal(4.5m, stats.AverageStars);
        Assert.Equal(2, stats.RatingCount);
        Assert.Equal(3, stats.CompletedTrips);
    }

    [Fact]
    public async Task GetRecentRatings_ReturnsListFromDb()
    {
        int userId = 2;
        var ratings = new List<RecentRatingDto>
        {
            new() { RaterName = "Johan van der Merwe", Stars = 5, Comment = "Great", CreatedAt = DateTime.UtcNow, RoleContext = "PH" }
        };
        _mockRatingDb.Setup(db => db.GetRecentRatings(userId, 5, null)).ReturnsAsync(ratings);

        var result = await _controller.GetRecentRatings(userId);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var list = Assert.IsType<List<RecentRatingDto>>(okResult.Value);
        Assert.Single(list);
        Assert.Equal("Johan van der Merwe", list[0].RaterName);
    }

    [Fact]
    public async Task GetRecentRatings_WithOutOfRangeTake_FallsBackToDefault()
    {
        int userId = 2;
        _mockRatingDb.Setup(db => db.GetRecentRatings(userId, 5, null)).ReturnsAsync(new List<RecentRatingDto>());

        var result = await _controller.GetRecentRatings(userId, take: 999);

        Assert.IsType<OkObjectResult>(result);
        _mockRatingDb.Verify(db => db.GetRecentRatings(userId, 5, null), Times.Once);
    }

    [Fact]
    public async Task DisputeRating_ByTheActualRatee_ReturnsOk()
    {
        SetCaller(2); // caller is the ratee
        _mockRatingDb.Setup(db => db.GetRatingRateeUserId(42)).ReturnsAsync(2);
        _mockRatingDb.Setup(db => db.CreateDispute(42, 2, "This is unfair")).ReturnsAsync(true);

        var result = await _controller.DisputeRating(42, new CreateDisputeRequest { Reason = "This is unfair" });

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task DisputeRating_WithoutReason_ReturnsBadRequest()
    {
        var result = await _controller.DisputeRating(42, new CreateDisputeRequest { Reason = "" });

        Assert.IsType<BadRequestObjectResult>(result);
        _mockRatingDb.Verify(db => db.GetRatingRateeUserId(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task DisputeRating_WhenRatingDoesNotExist_ReturnsNotFound()
    {
        _mockRatingDb.Setup(db => db.GetRatingRateeUserId(42)).ReturnsAsync(0);

        var result = await _controller.DisputeRating(42, new CreateDisputeRequest { Reason = "Unfair" });

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task DisputeRating_WhenCallerIsNotTheRatee_ReturnsForbidden()
    {
        SetCaller(1); // caller is not the ratee (2) of this rating
        _mockRatingDb.Setup(db => db.GetRatingRateeUserId(42)).ReturnsAsync(2);

        var result = await _controller.DisputeRating(42, new CreateDisputeRequest { Reason = "Unfair" });

        var statusResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, statusResult.StatusCode);
        _mockRatingDb.Verify(db => db.CreateDispute(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [InlineData("Denied")]
    [InlineData("Upheld")]
    public async Task DisputeRating_WhenAlreadyResolved_ReturnsBadRequestWithSpecificMessage(string resolvedStatus)
    {
        SetCaller(2);
        _mockRatingDb.Setup(db => db.GetRatingRateeUserId(42)).ReturnsAsync(2);
        _mockRatingDb.Setup(db => db.CreateDispute(42, 2, "Trying again")).ReturnsAsync(false);
        _mockRatingDb.Setup(db => db.GetDisputeStatus(42)).ReturnsAsync(resolvedStatus);

        var result = await _controller.DisputeRating(42, new CreateDisputeRequest { Reason = "Trying again" });

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var value = badRequest.Value!.ToString();
        Assert.Contains("reeds hersien", value);
    }
}
