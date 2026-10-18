using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using WebAPI.Controllers;
using WebAPI.Database;
using WebAPI.Models;
using Xunit;

namespace WebAPI.Tests.Controllers;

public class BookingCompanionsControllerTests
{
    private readonly Mock<BookingCompanions_db> _db = new("DummyConnectionString");
    private readonly Mock<ResourceOwnership_db> _ownership = new("DummyConnectionString");
    private readonly Mock<WebAPI.Services.IEmailService> _email = new();

    private BookingCompanionsController Controller(int? userId, params string[] roles)
    {
        var controller = new BookingCompanionsController(_db.Object, _email.Object, _ownership.Object);
        var claims = new List<Claim>();
        if (userId != null) claims.Add(new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString()));
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        var identity = userId == null ? new ClaimsIdentity() : new ClaimsIdentity(claims, "TestAuth");
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) } };
        return controller;
    }

    private static BookingHuntersResponse Roster() => new() { BookingId = 5, BookingStatus = "Approved", MaxHunters = 3 };

    [Fact]
    public async Task Unauthenticated_callers_get_401()
    {
        var c = Controller(null);
        Assert.IsType<UnauthorizedResult>(await c.GetHunters(5));
        Assert.IsType<UnauthorizedResult>(await c.AddHunter(5, new AddBookingHunterRequest { Name = "A", Email = "a@b.co" }));
        Assert.IsType<UnauthorizedResult>(await c.RemoveHunter(5, 1));
        Assert.IsType<UnauthorizedResult>(await c.GetCompanionBookings());
    }

    [Fact]
    public async Task Roster_is_for_people_on_the_booking_and_its_staff_only()
    {
        _db.Setup(d => d.GetBookingHunters(5, It.IsAny<int>(), false)).ReturnsAsync((Roster(), false));
        _db.Setup(d => d.GetBookingHunters(5, 10, false)).ReturnsAsync((Roster(), true));
        _ownership.Setup(o => o.GetBookingParticipantUserIds(5)).ReturnsAsync(new HashSet<int> { 10, 20 });

        Assert.IsType<OkObjectResult>(await Controller(10).GetHunters(5)); // main hunter / added hunter
        Assert.IsType<OkObjectResult>(await Controller(20).GetHunters(5)); // e.g. the PJ or PH
        Assert.IsType<ForbidResult>(await Controller(99).GetHunters(5));   // a stranger
    }

    [Fact]
    public async Task A_missing_booking_does_not_reveal_whether_it_exists()
    {
        _db.Setup(d => d.GetBookingHunters(5, 99, false)).ReturnsAsync(((BookingHuntersResponse?)null, false));
        Assert.IsType<ForbidResult>(await Controller(99).GetHunters(5));
    }

    [Theory]
    [InlineData("", "a@b.co")]
    [InlineData("Jan", "not-an-email")]
    [InlineData("Jan", "")]
    public async Task Add_validates_name_and_email(string name, string email)
    {
        var result = await Controller(10).AddHunter(5, new AddBookingHunterRequest { Name = name, Email = email });
        Assert.IsType<BadRequestObjectResult>(result);
        _db.Verify(d => d.AddCompanion(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [InlineData(AddCompanionOutcome.NotMainHunter, typeof(ForbidResult))]
    [InlineData(AddCompanionOutcome.NotConfirmed, typeof(ConflictObjectResult))]
    [InlineData(AddCompanionOutcome.Full, typeof(ConflictObjectResult))]
    [InlineData(AddCompanionOutcome.NotAHunter, typeof(ConflictObjectResult))]
    [InlineData(AddCompanionOutcome.AlreadyAdded, typeof(ConflictObjectResult))]
    [InlineData(AddCompanionOutcome.IsMainHunter, typeof(ConflictObjectResult))]
    [InlineData(AddCompanionOutcome.BookingNotFound, typeof(ForbidResult))]
    public async Task Refused_adds_map_to_clear_responses_and_send_no_email(AddCompanionOutcome outcome, Type expected)
    {
        _db.Setup(d => d.AddCompanion(5, 10, false, "Jan", "jan@b.co")).ReturnsAsync(new AddCompanionResult(outcome));
        var result = await Controller(10).AddHunter(5, new AddBookingHunterRequest { Name = "Jan", Email = "jan@b.co" });
        Assert.IsType(expected, result);
        _email.Verify(e => e.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    [Theory]
    [InlineData(AddCompanionOutcome.Linked, "/jagter/besprekings")]
    [InlineData(AddCompanionOutcome.Invited, "/registreer?email=jan%40b.co")]
    public async Task A_successful_add_emails_the_hunter(AddCompanionOutcome outcome, string expectedLink)
    {
        var hunter = new BookingHunterDto { CompanionId = 7, Name = "Jan <b>", Email = "jan@b.co", Status = outcome == AddCompanionOutcome.Linked ? "Linked" : "Pending" };
        _db.Setup(d => d.AddCompanion(5, 10, false, "Jan", "jan@b.co")).ReturnsAsync(new AddCompanionResult(outcome, hunter, "Koedoe Safari", "Piet Pompies"));

        var result = await Controller(10).AddHunter(5, new AddBookingHunterRequest { Name = " Jan ", Email = " jan@b.co " });

        Assert.IsType<OkObjectResult>(result);
        _email.Verify(e => e.SendEmailAsync("jan@b.co", It.IsAny<string>(),
            It.Is<string>(body => body.Contains(expectedLink) && body.Contains("Jan &lt;b&gt;") && !body.Contains("Jan <b>")), true), Times.Once);
    }

    [Fact]
    public async Task Admins_act_with_admin_rights()
    {
        _db.Setup(d => d.AddCompanion(5, 1, true, "Jan", "jan@b.co")).ReturnsAsync(new AddCompanionResult(AddCompanionOutcome.NotConfirmed));
        await Controller(1, "Admin").AddHunter(5, new AddBookingHunterRequest { Name = "Jan", Email = "jan@b.co" });
        _db.Verify(d => d.AddCompanion(5, 1, true, "Jan", "jan@b.co"), Times.Once);
    }

    [Theory]
    [InlineData(RemoveCompanionOutcome.Removed, typeof(OkObjectResult))]
    [InlineData(RemoveCompanionOutcome.NotFound, typeof(NotFoundResult))]
    [InlineData(RemoveCompanionOutcome.Forbidden, typeof(ForbidResult))]
    [InlineData(RemoveCompanionOutcome.Locked, typeof(ConflictObjectResult))]
    public async Task Remove_maps_outcomes(RemoveCompanionOutcome outcome, Type expected)
    {
        _db.Setup(d => d.RemoveCompanion(5, 7, 10, false)).ReturnsAsync(outcome);
        Assert.IsType(expected, await Controller(10).RemoveHunter(5, 7));
    }

    [Fact]
    public async Task Companion_bookings_are_the_callers_own()
    {
        _db.Setup(d => d.GetCompanionBookings(10)).ReturnsAsync(new List<CompanionBookingResponse> { new() { Id = 5 } });
        var ok = Assert.IsType<OkObjectResult>(await Controller(10).GetCompanionBookings());
        Assert.Single((List<CompanionBookingResponse>)ok.Value!);
        _db.Verify(d => d.GetCompanionBookings(It.Is<int>(id => id != 10)), Times.Never);
    }
}
