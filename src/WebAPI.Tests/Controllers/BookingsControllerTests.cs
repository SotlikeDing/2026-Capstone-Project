using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using WebAPI.Controllers;
using WebAPI.Database;
using WebAPI.Models;
using Xunit;

namespace WebAPI.Tests.Controllers;

public class BookingsControllerTests
{
    private readonly Mock<Bookings_db> _mockDb;
    private readonly BookingsController _controller;

    public BookingsControllerTests()
    {
        _mockDb = new Mock<Bookings_db>("DummyConnectionString");
        // Default: any userId/role passed to ApproveWithStaff is a legitimate staff
        // member unless a specific test overrides this to exercise the validation path.
        _mockDb.Setup(db => db.IsValidStaffAssignee(It.IsAny<int>(), It.IsAny<string>())).ReturnsAsync(true);
        _controller = new BookingsController(_mockDb.Object);
        // Every booking-mutating action now requires an authenticated caller who owns
        // the booking (or holds a relevant staff role) - default the shared controller
        // to an authenticated Admin caller so it satisfies every ownership/role check
        // uniformly, since these tests are about business logic, not authorization.
        // Authorization itself is covered by dedicated tests further down.
        SetCaller(_controller, 1, "Admin");
    }

    private static void SetCaller(BookingsController controller, int userId, params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()) };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        var httpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth")) };
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
    }

    private static BookingsController NewControllerWithoutCaller()
    {
        var mockDb = new Mock<Bookings_db>("DummyConnectionString");
        var controller = new BookingsController(mockDb.Object);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        return controller;
    }

    // ── Authorization guard tests ────────────────────────────────────────────
    // These endpoints used to have no [Authorize]/ownership check at all - anyone,
    // authenticated or not, could approve/reject/pay/delete any booking. Each of
    // these proves the fix actually closes that hole.

    [Theory]
    [InlineData("ApproveBooking")]
    [InlineData("RejectBooking")]
    [InlineData("ApproveWithStaff")]
    [InlineData("CreateBooking")]
    [InlineData("DeleteBooking")]
    public async Task MutatingBookingEndpoints_WhenCallerHasNoIdentity_ReturnUnauthorized(string action)
    {
        var controller = NewControllerWithoutCaller();

        IActionResult result = action switch
        {
            "ApproveBooking" => await controller.ApproveBooking(101, "Approved"),
            "RejectBooking" => await controller.RejectBooking(101, new RejectBookingRequest()),
            "ApproveWithStaff" => await controller.ApproveWithStaff(101, new ApproveWithStaffRequest { PhUserId = 15 }),
            "CreateBooking" => await controller.CreateBooking(new CreateBookingRequest { HunterId = 2, PakketId = 5 }),
            "DeleteBooking" => await controller.DeleteBooking(101, 5),
            _ => throw new InvalidOperationException()
        };

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task ApproveBooking_WhenCallerIsNeitherHunterNorPjNorStaff_Returns403()
    {
        var mockDb = new Mock<Bookings_db>("DummyConnectionString");
        mockDb.Setup(db => db.GetSpecificBooking(101)).ReturnsAsync(new BookingResponse { Id = 101, UserId = 7, PjUserId = 8 });
        var controller = new BookingsController(mockDb.Object);
        SetCaller(controller, 999); // a random authenticated user, not the hunter or the PJ, and no staff role

        var result = await controller.ApproveBooking(101, "Approved");

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, status.StatusCode);
        mockDb.Verify(db => db.ApproveBooking(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ApproveBooking_WhenTheBookingsOwnHunterCancelsBeforePaying_Succeeds()
    {
        var mockDb = new Mock<Bookings_db>("DummyConnectionString");
        mockDb.Setup(db => db.GetSpecificBooking(101)).ReturnsAsync(new BookingResponse { Id = 101, UserId = 7, PjUserId = 8, Status = "Approved" });
        mockDb.Setup(db => db.ApproveBooking(101, "Cancelled")).ReturnsAsync(true);
        var controller = new BookingsController(mockDb.Object);
        SetCaller(controller, 7); // the hunter cancelling their own booking

        var result = await controller.ApproveBooking(101, "Cancelled");

        Assert.IsType<OkResult>(result);
    }

    // The hunter used to be able to set ANY status on their own booking - including
    // "Approved" (skipping the outfitter) and "Paid" (skipping payment).
    [Theory]
    [InlineData("Approved")]
    [InlineData("Paid")]
    [InlineData("Completed")]
    [InlineData("Rejected")]
    public async Task ApproveBooking_WhenTheHunterSetsAnythingButCancelled_Returns403(string status)
    {
        var mockDb = new Mock<Bookings_db>("DummyConnectionString");
        mockDb.Setup(db => db.GetSpecificBooking(101)).ReturnsAsync(new BookingResponse { Id = 101, UserId = 7, PjUserId = 8, Status = "Pending" });
        var controller = new BookingsController(mockDb.Object);
        SetCaller(controller, 7);

        var result = await controller.ApproveBooking(101, status);

        Assert.Equal(403, Assert.IsType<ObjectResult>(result).StatusCode);
        mockDb.Verify(db => db.ApproveBooking(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [InlineData("Paid")]
    [InlineData("Completed")]
    public async Task ApproveBooking_WhenTheHunterCancelsAfterPaying_Returns409(string currentStatus)
    {
        var mockDb = new Mock<Bookings_db>("DummyConnectionString");
        mockDb.Setup(db => db.GetSpecificBooking(101)).ReturnsAsync(new BookingResponse { Id = 101, UserId = 7, PjUserId = 8, Status = currentStatus });
        var controller = new BookingsController(mockDb.Object);
        SetCaller(controller, 7);

        var result = await controller.ApproveBooking(101, "Cancelled");

        Assert.IsType<ConflictObjectResult>(result);
        mockDb.Verify(db => db.ApproveBooking(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ApproveBooking_WhenThePjSetsPaid_Returns403()
    {
        var mockDb = new Mock<Bookings_db>("DummyConnectionString");
        mockDb.Setup(db => db.GetSpecificBooking(101)).ReturnsAsync(new BookingResponse { Id = 101, UserId = 7, PjUserId = 8, Status = "Approved" });
        var controller = new BookingsController(mockDb.Object);
        SetCaller(controller, 8);

        var result = await controller.ApproveBooking(101, "Paid");

        Assert.Equal(403, Assert.IsType<ObjectResult>(result).StatusCode);
        mockDb.Verify(db => db.ApproveBooking(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task RejectBooking_WhenTheHunterTriesIt_Returns403()
    {
        var mockDb = new Mock<Bookings_db>("DummyConnectionString");
        mockDb.Setup(db => db.GetSpecificBooking(101)).ReturnsAsync(new BookingResponse { Id = 101, UserId = 7, PjUserId = 8, Status = "Pending" });
        var controller = new BookingsController(mockDb.Object);
        SetCaller(controller, 7);

        var result = await controller.RejectBooking(101, new RejectBookingRequest());

        Assert.Equal(403, Assert.IsType<ObjectResult>(result).StatusCode);
    }

    // Payment status and price/status corrections are Admin-only: the [Authorize(Roles)]
    // attribute is what enforces it, so check it is there.
    [Theory]
    [InlineData(nameof(BookingsController.SetPaymentStatus))]
    [InlineData(nameof(BookingsController.UpdateBooking))]
    public void PaymentAndCorrectionEndpoints_AreAdminOnly(string action)
    {
        var method = typeof(BookingsController).GetMethod(action)!;
        var attr = method.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), false)
            .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>().Single();
        Assert.Equal("Admin", attr.Roles);
    }

    [Fact]
    public async Task ApproveBooking_WhenCallerIsThePackagesPj_Succeeds()
    {
        var mockDb = new Mock<Bookings_db>("DummyConnectionString");
        mockDb.Setup(db => db.GetSpecificBooking(101)).ReturnsAsync(new BookingResponse { Id = 101, UserId = 7, PjUserId = 8 });
        mockDb.Setup(db => db.ApproveBooking(101, "Approved")).ReturnsAsync(true);
        var controller = new BookingsController(mockDb.Object);
        SetCaller(controller, 8); // the PJ who owns this booking's package

        var result = await controller.ApproveBooking(101, "Approved");

        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task ApproveWithStaff_WhenCallerIsNotAPj_Returns403()
    {
        var controller = new BookingsController(_mockDb.Object);
        SetCaller(controller, 5); // authenticated, but no PJ/Admin role

        var result = await controller.ApproveWithStaff(101, new ApproveWithStaffRequest { PhUserId = 15 });

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, status.StatusCode);
        _mockDb.Verify(db => db.ApproveBookingWithStaff(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<decimal?>(), It.IsAny<decimal?>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task CreateBooking_IgnoresClientSuppliedHunterId_UsesCallersOwnIdentity()
    {
        // A malicious/buggy client sending someone else's HunterId must not be able to
        // create a booking "as" that other hunter - the server derives it from the JWT.
        var mockDb = new Mock<Bookings_db>("DummyConnectionString");
        mockDb.Setup(db => db.CreateBooking(It.IsAny<CreateBookingRequest>(), 3, 5, 0)).ReturnsAsync(301);
        var controller = new BookingsController(mockDb.Object);
        SetCaller(controller, 3);

        var result = await controller.CreateBooking(new CreateBookingRequest { HunterId = 999, PakketId = 5, StartDate = DateTime.UtcNow.AddDays(10), EndDate = DateTime.UtcNow.AddDays(12) });

        Assert.IsType<OkObjectResult>(result);
        mockDb.Verify(db => db.CreateBooking(It.IsAny<CreateBookingRequest>(), 3, 5, 0), Times.Once);
        mockDb.Verify(db => db.CreateBooking(It.IsAny<CreateBookingRequest>(), 999, It.IsAny<int>(), It.IsAny<decimal>()), Times.Never);
    }

    [Fact]
    public async Task DeleteBooking_IgnoresRouteUserId_UsesCallersOwnIdentity()
    {
        var mockDb = new Mock<Bookings_db>("DummyConnectionString");
        mockDb.Setup(db => db.DeleteBooking(101, 3)).ReturnsAsync(CancelBookingResult.Cancelled);
        var controller = new BookingsController(mockDb.Object);
        SetCaller(controller, 3);

        // Route says userId=999 (spoofed) - the real caller (3) is what must be used.
        var result = await controller.DeleteBooking(101, 999);

        Assert.IsType<OkObjectResult>(result);
        mockDb.Verify(db => db.DeleteBooking(101, 3), Times.Once);
        mockDb.Verify(db => db.DeleteBooking(101, 999), Times.Never);
    }

    [Fact]
    public async Task ApproveWithStaff_WhenPhUserIdIsZero_ReturnsBadRequest()
    {
        // Arrange
        var req = new ApproveWithStaffRequest { PhUserId = 0, VgUserId = null };

        // Act
        var result = await _controller.ApproveWithStaff(101, req);

        // Assert
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(badRequest.Value);
        Assert.Contains("PH Gids is verpligtend", badRequest.Value.ToString());
    }

    [Fact]
    public async Task ApproveWithStaff_WhenValid_ReturnsOk()
    {
        // Arrange
        var req = new ApproveWithStaffRequest { PhUserId = 15, VgUserId = 20 };
        _mockDb.Setup(db => db.ApproveBookingWithStaff(101, 15, 20, null, null, null)).ReturnsAsync(true);

        // Act
        var result = await _controller.ApproveWithStaff(101, req);

        // Assert
        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
    }

    [Fact]
    public async Task ApproveWithStaff_WithConfirmedPricing_PassesPricingToDb()
    {
        // Arrange
        var req = new ApproveWithStaffRequest
        {
            PhUserId = 15,
            VgUserId = 20,
            ConfirmedTotalPrice = 25000m,
            ConfirmedAccommodationPrice = 9000m,
            PriceBreakdownJson = "{\"accommodationTotal\":9000}"
        };
        _mockDb.Setup(db => db.ApproveBookingWithStaff(101, 15, 20, 25000m, 9000m, "{\"accommodationTotal\":9000}"))
               .ReturnsAsync(true);

        // Act
        var result = await _controller.ApproveWithStaff(101, req);

        // Assert
        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
        _mockDb.Verify(db => db.ApproveBookingWithStaff(101, 15, 20, 25000m, 9000m, "{\"accommodationTotal\":9000}"), Times.Once);
    }

    [Fact]
    public async Task ApproveWithStaff_WhenDbReturnsFalse_ReturnsNotFound()
    {
        // Arrange
        var req = new ApproveWithStaffRequest { PhUserId = 15, VgUserId = null };
        _mockDb.Setup(db => db.ApproveBookingWithStaff(999, 15, null, null, null, null)).ReturnsAsync(false);

        // Act
        var result = await _controller.ApproveWithStaff(999, req);

        // Assert
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task ApproveWithStaff_WhenPhUserIdIsNotARealPh_ReturnsBadRequest()
    {
        // Arrange - a userId that doesn't hold the PH role must never be allowed
        // through, otherwise the booking gets stranded on an assignee who'll never see
        // a "please respond" notification on any dashboard.
        var req = new ApproveWithStaffRequest { PhUserId = 15, VgUserId = null };
        _mockDb.Setup(db => db.IsValidStaffAssignee(15, "PH")).ReturnsAsync(false);

        // Act
        var result = await _controller.ApproveWithStaff(101, req);

        // Assert
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(badRequest.Value);
        Assert.Contains("nie 'n geregistreerde PH gids nie", badRequest.Value.ToString());
        _mockDb.Verify(db => db.ApproveBookingWithStaff(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<decimal?>(), It.IsAny<decimal?>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task ApproveWithStaff_WhenVgUserIdIsNotARealVg_ReturnsBadRequest()
    {
        // Arrange
        var req = new ApproveWithStaffRequest { PhUserId = 15, VgUserId = 20 };
        _mockDb.Setup(db => db.IsValidStaffAssignee(20, "VG")).ReturnsAsync(false);

        // Act
        var result = await _controller.ApproveWithStaff(101, req);

        // Assert
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(badRequest.Value);
        Assert.Contains("nie 'n geregistreerde videograaf nie", badRequest.Value.ToString());
        _mockDb.Verify(db => db.ApproveBookingWithStaff(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<decimal?>(), It.IsAny<decimal?>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task GetBookings_ReturnsOkWithList()
    {
        // Arrange
        var list = new List<BookingResponse>
        {
            new BookingResponse
            {
                Id = 101,
                PakketId = 5,
                PakketName = "Waterberg Safari",
                UserId = 2,
                UserName = "Kobus Jagter",
                TotalAmount = 9000,
                Status = "Pending"
            }
        };
        _mockDb.Setup(db => db.GetBookings(1)).ReturnsAsync(list);

        // Act
        var result = await _controller.GetBookings(1);

        // Assert
        var ok = Assert.IsType<OkObjectResult>(result);
        var resList = Assert.IsType<List<BookingResponse>>(ok.Value);
        Assert.Single(resList);
        Assert.Equal("Waterberg Safari", resList[0].PakketName);
    }

    [Fact]
    public async Task ApproveBooking_WhenStatusApproved_ReturnsOk()
    {
        // Arrange
        _mockDb.Setup(db => db.ApproveBooking(101, "Approved")).ReturnsAsync(true);

        // Act
        var result = await _controller.ApproveBooking(101, "Approved");

        // Assert
        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task ApproveBooking_WhenStatusRejected_ReturnsOk()
    {
        // Arrange
        _mockDb.Setup(db => db.ApproveBooking(101, "Rejected")).ReturnsAsync(true);

        // Act
        var result = await _controller.ApproveBooking(101, "Rejected");

        // Assert
        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task ApproveBooking_WhenStatusCancelled_ReturnsOk()
    {
        // Arrange
        _mockDb.Setup(db => db.ApproveBooking(101, "Cancelled")).ReturnsAsync(true);

        // Act
        var result = await _controller.ApproveBooking(101, "Cancelled");

        // Assert
        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task ApproveBooking_WhenNotFound_ReturnsNotFound()
    {
        // Arrange
        _mockDb.Setup(db => db.ApproveBooking(999, "Rejected")).ReturnsAsync(false);

        // Act
        var result = await _controller.ApproveBooking(999, "Rejected");

        // Assert
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task DeleteBooking_WhenSuccess_ReturnsOk()
    {
        // Arrange - the route's userId is spoofable and now ignored; the caller's own
        // authenticated identity (5) is what's actually passed to the DB layer.
        SetCaller(_controller, 5);
        _mockDb.Setup(db => db.DeleteBooking(101, 5)).ReturnsAsync(CancelBookingResult.Cancelled);

        // Act
        var result = await _controller.DeleteBooking(101, 5);

        // Assert
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task DeleteBooking_WhenAlreadyApprovedOrPaid_ReturnsConflict()
    {
        SetCaller(_controller, 5);
        _mockDb.Setup(db => db.DeleteBooking(101, 5)).ReturnsAsync(CancelBookingResult.NotCancellable);

        var result = await _controller.DeleteBooking(101, 5);

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public async Task DeleteBooking_WhenUnauthorized_ReturnsForbid()
    {
        // Arrange
        _mockDb.Setup(db => db.DeleteBooking(101, 99)).ReturnsAsync(CancelBookingResult.NotOwner);

        // Act
        var result = await _controller.DeleteBooking(101, 99);

        // Assert
        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task SetPaymentStatus_WhenApplied_ReturnsOkWithAlreadyPaidFalse()
    {
        // Arrange
        _mockDb.Setup(db => db.SetPaymentStatus(101, "Paid")).ReturnsAsync(Bookings_db.PaymentStatusResult.Applied);

        // Act
        var result = await _controller.SetPaymentStatus(101, "Paid");

        // Assert
        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Contains("alreadyPaid = False", ok.Value!.ToString());
    }

    [Fact]
    public async Task SetPaymentStatus_WhenBookingNotFound_ReturnsNotFound()
    {
        // Arrange
        _mockDb.Setup(db => db.SetPaymentStatus(999, "Paid")).ReturnsAsync(Bookings_db.PaymentStatusResult.NotFound);

        // Act
        var result = await _controller.SetPaymentStatus(999, "Paid");

        // Assert
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task SetPaymentStatus_WhenAlreadyPaid_ReturnsOkWithAlreadyPaidTrue_AndDoesNotThrow()
    {
        // A duplicate call (reloaded PayFast return URL, retried ITN, two tabs) must
        // report AlreadyPaid rather than silently look like a fresh success.
        _mockDb.Setup(db => db.SetPaymentStatus(101, "Paid")).ReturnsAsync(Bookings_db.PaymentStatusResult.AlreadyPaid);

        var result = await _controller.SetPaymentStatus(101, "Paid");

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Contains("alreadyPaid = True", ok.Value!.ToString());
    }

    [Fact]
    public async Task UpdateBooking_WhenSuccess_ReturnsOk()
    {
        // Arrange
        var req = new UpdateBookingRequest { Status = "Completed" };
        _mockDb.Setup(db => db.UpdateBooking(101, req)).ReturnsAsync(true);

        // Act
        var result = await _controller.UpdateBooking(101, req);

        // Assert
        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<WebAPI.Controllers.BookingsResponse>(ok.Value);
        Assert.True(resp.Success);
    }

    [Fact]
    public async Task RejectBooking_WhenSuccess_ReturnsOk()
    {
        // Arrange
        var req = new RejectBookingRequest { ReasonCode = "DATES_FULL", ReasonDescription = "Plaas vol bespreek", Notes = "Geen plek beskikbaar nie" };
        _mockDb.Setup(db => db.RejectBookingWithReason(101, "Plaas vol bespreek", "Geen plek beskikbaar nie")).ReturnsAsync(true);

        // Act
        var result = await _controller.RejectBooking(101, req);

        // Assert
        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
    }

    [Fact]
    public async Task RejectBooking_WhenNotFound_ReturnsNotFound()
    {
        // Arrange
        var req = new RejectBookingRequest { ReasonCode = "OTHER", ReasonDescription = "Ander rede" };
        _mockDb.Setup(db => db.RejectBookingWithReason(999, "Ander rede", null)).ReturnsAsync(false);

        // Act
        var result = await _controller.RejectBooking(999, req);

        // Assert
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetHunterBookings_WhenCalled_ReturnsOkWithRejectionDetails()
    {
        // Arrange
        var list = new List<BookingResponse>
        {
            new BookingResponse
            {
                Id = 202,
                PakketId = 7,
                PakketName = "Kalahari Safari",
                FarmName = "Kalahari Plaas",
                Status = "Rejected",
                RejectionReason = "Plaas is reeds vol bespreek op hierdie datums",
                RejectionNotes = "Probeer asseblief volgende maand.",
                TotalAmount = 7500,
                PaymentReference = "MP-EXP-000202"
            }
        };
        _mockDb.Setup(db => db.GetHunterBookings(10)).ReturnsAsync(list);

        // Act
        var result = await _controller.GetHunterBookings(10);

        // Assert
        var ok = Assert.IsType<OkObjectResult>(result);
        var resList = Assert.IsType<List<BookingResponse>>(ok.Value);
        Assert.Single(resList);
        Assert.Equal("Rejected", resList[0].Status);
        Assert.Equal("Plaas is reeds vol bespreek op hierdie datums", resList[0].RejectionReason);
        Assert.Equal("Probeer asseblief volgende maand.", resList[0].RejectionNotes);
        Assert.Equal("MP-EXP-000202", resList[0].PaymentReference);
    }

    [Fact]
    public async Task GetHunterBookings_WhenNoBookings_ReturnsEmptyList()
    {
        // Arrange
        _mockDb.Setup(db => db.GetHunterBookings(99)).ReturnsAsync(new List<BookingResponse>());

        // Act
        var result = await _controller.GetHunterBookings(99);

        // Assert
        var ok = Assert.IsType<OkObjectResult>(result);
        var resList = Assert.IsType<List<BookingResponse>>(ok.Value);
        Assert.Empty(resList);
    }

    [Fact]
    public async Task ApproveWithStaff_WhenSuccessful_DoesNotDispatchVoucherEmail()
    {
        // Assigning staff no longer approves the booking outright - the voucher now
        // fires from StaffRespond once every assigned role has actually accepted.
        var mockEmail = new Mock<WebAPI.Services.IEmailService>();
        var controller = new BookingsController(_mockDb.Object, mockEmail.Object);
        SetCaller(controller, 1, "Admin");

        var req = new ApproveWithStaffRequest
        {
            PhUserId = 15,
            VgUserId = 20,
            ConfirmedTotalPrice = 15000m,
            ConfirmedAccommodationPrice = 5000m
        };
        _mockDb.Setup(db => db.ApproveBookingWithStaff(101, 15, 20, 15000m, 5000m, null)).ReturnsAsync(true);

        var result = await controller.ApproveWithStaff(101, req);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
        mockEmail.Verify(e => e.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
        _mockDb.Verify(db => db.GetSpecificBooking(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task StaffRespond_WhenCallerHasNoIdentity_ReturnsUnauthorized()
    {
        var controller = new BookingsController(_mockDb.Object);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        var req = new RespondStaffAssignmentRequest { Role = "PH", Accept = true };

        var result = await controller.StaffRespond(101, req);

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task StaffRespond_WhenCallerIsNotThePendingAssignee_Returns403()
    {
        var controller = new BookingsController(_mockDb.Object);
        SetCaller(controller, 999);
        var req = new RespondStaffAssignmentRequest { Role = "PH", Accept = true };
        _mockDb.Setup(db => db.RespondToStaffAssignment(101, "PH", 999, true, null))
               .ReturnsAsync((Bookings_db.StaffResponseResult.Forbidden, false));

        var result = await controller.StaffRespond(101, req);

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, status.StatusCode);
    }

    [Fact]
    public async Task StaffRespond_WhenNoPendingAssignmentForRole_ReturnsNotFound()
    {
        var controller = new BookingsController(_mockDb.Object);
        SetCaller(controller, 15);
        var req = new RespondStaffAssignmentRequest { Role = "PH", Accept = false, DeclineReason = "Nie beskikbaar nie" };
        _mockDb.Setup(db => db.RespondToStaffAssignment(101, "PH", 15, false, "Nie beskikbaar nie"))
               .ReturnsAsync((Bookings_db.StaffResponseResult.NotFound, false));

        var result = await controller.StaffRespond(101, req);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task StaffRespond_WhenAcceptedButBookingNotYetFullyApproved_ReturnsOkWithoutEmail()
    {
        var mockEmail = new Mock<WebAPI.Services.IEmailService>();
        var controller = new BookingsController(_mockDb.Object, mockEmail.Object);
        SetCaller(controller, 15);
        var req = new RespondStaffAssignmentRequest { Role = "PH", Accept = true };
        _mockDb.Setup(db => db.RespondToStaffAssignment(101, "PH", 15, true, null))
               .ReturnsAsync((Bookings_db.StaffResponseResult.Ok, false));

        var result = await controller.StaffRespond(101, req);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
        mockEmail.Verify(e => e.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task StaffRespond_WhenBookingNowFullyApproved_DispatchesVoucherEmailToHunter()
    {
        var mockEmail = new Mock<WebAPI.Services.IEmailService>();
        var controller = new BookingsController(_mockDb.Object, mockEmail.Object);
        SetCaller(controller, 20);
        var req = new RespondStaffAssignmentRequest { Role = "VG", Accept = true };
        _mockDb.Setup(db => db.RespondToStaffAssignment(101, "VG", 20, true, null))
               .ReturnsAsync((Bookings_db.StaffResponseResult.Ok, true));

        var booking = new BookingResponse
        {
            Id = 101,
            HunterEmail = "hunter@example.com",
            UserName = "Johan Jagter",
            PakketName = "Bosveld Trofee",
            FarmName = "Kudu Kop",
            StartDate = DateTime.Today.AddDays(10),
            EndDate = DateTime.Today.AddDays(15),
            TotalAmount = 15000m,
            PaymentReference = "MP-EXP-000101",
            BookedSpecies = new List<string> { "Kudu" }
        };
        _mockDb.Setup(db => db.GetSpecificBooking(101)).ReturnsAsync(booking);

        var result = await controller.StaffRespond(101, req);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
        mockEmail.Verify(e => e.SendEmailAsync(
            "hunter@example.com",
            It.Is<string>(s => s.Contains("MP-EXP-000101")),
            It.IsAny<string>(),
            true
        ), Times.Once);
    }

    [Fact]
    public async Task StaffRespond_WhenDeclined_ReturnsOkWithoutEmail()
    {
        var mockEmail = new Mock<WebAPI.Services.IEmailService>();
        var controller = new BookingsController(_mockDb.Object, mockEmail.Object);
        SetCaller(controller, 15);
        var req = new RespondStaffAssignmentRequest { Role = "PH", Accept = false, DeclineReason = "Nie beskikbaar nie" };
        _mockDb.Setup(db => db.RespondToStaffAssignment(101, "PH", 15, false, "Nie beskikbaar nie"))
               .ReturnsAsync((Bookings_db.StaffResponseResult.Ok, false));

        var result = await controller.StaffRespond(101, req);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
        mockEmail.Verify(e => e.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task StaffRespond_WhenRoleIsNeitherPhNorVg_ReturnsBadRequest()
    {
        // A tampered/garbage role string must never silently fall through to PH - it
        // has to be rejected outright rather than acting on the wrong role.
        var controller = new BookingsController(_mockDb.Object);
        SetCaller(controller, 15);
        var req = new RespondStaffAssignmentRequest { Role = "SomethingElse", Accept = true };

        var result = await controller.StaffRespond(101, req);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(badRequest.Value);
        Assert.Contains("PH of VG", badRequest.Value.ToString());
        _mockDb.Verify(db => db.RespondToStaffAssignment(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task CreateBooking_WithBookedSpecies_PassesToDatabase()
    {
        // Arrange - hunterId is now always derived from the caller's own authenticated
        // identity (2), not the client-supplied request.HunterId, so the caller here
        // must actually be user 2 for the DB call to be made with hunterId=2.
        SetCaller(_controller, 2);
        var req = new CreateBookingRequest
        {
            PakketId = 5,
            HunterId = 2,
            StartDate = DateTime.Today.AddDays(7),
            EndDate = DateTime.Today.AddDays(12),
            HuntGuests = 2,
            NonHuntGuests = 1,
            FirearmOption = "Rental",
            SpecialNotes = "Vegetarian meal required",
            WantsVideographer = true,
            BookedSpecies = new List<string> { "Kudu", "Eland" }
        };

        _mockDb.Setup(db => db.CreateBooking(req, 2, 5, 0)).ReturnsAsync(301);

        // Act
        var result = await _controller.CreateBooking(req);

        // Assert
        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
        _mockDb.Verify(db => db.CreateBooking(It.Is<CreateBookingRequest>(r =>
            r.WantsVideographer &&
            r.BookedSpecies != null &&
            r.BookedSpecies.Count == 2
        ), 2, 5, 0), Times.Once);
    }

    [Fact]
    public async Task CreateBooking_RejectsWhenTargetFarmerHasUnverifiedRequiredDocuments()
    {
        // Defense-in-depth: even though Package_db already hides such packages from
        // public browse, CreateBooking must independently refuse one that slips
        // through anyway (a stale client view, a direct API call, etc).
        var mockDb = new Mock<Bookings_db>("DummyConnectionString");
        var mockDocumentsDb = new Mock<UserDocuments_db>("DummyConnectionString");
        mockDb.Setup(db => db.GetFarmerUserRoleIdForPackage(5)).ReturnsAsync(42);
        mockDocumentsDb.Setup(db => db.AreRequiredDocumentsApproved(42)).ReturnsAsync(false);

        var controller = new BookingsController(mockDb.Object, documentsDb: mockDocumentsDb.Object);
        SetCaller(controller, 2);

        var req = new CreateBookingRequest { PakketId = 5, StartDate = DateTime.Today.AddDays(7), EndDate = DateTime.Today.AddDays(12) };
        var result = await controller.CreateBooking(req);

        Assert.IsType<BadRequestObjectResult>(result);
        mockDb.Verify(db => db.CreateBooking(It.IsAny<CreateBookingRequest>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<decimal>()), Times.Never);
    }

    [Fact]
    public async Task CreateBooking_SucceedsWhenTargetFarmerIsFullyVerified()
    {
        var mockDb = new Mock<Bookings_db>("DummyConnectionString");
        var mockDocumentsDb = new Mock<UserDocuments_db>("DummyConnectionString");
        mockDb.Setup(db => db.GetFarmerUserRoleIdForPackage(5)).ReturnsAsync(42);
        mockDocumentsDb.Setup(db => db.AreRequiredDocumentsApproved(42)).ReturnsAsync(true);
        mockDb.Setup(db => db.CreateBooking(It.IsAny<CreateBookingRequest>(), 2, 5, 0)).ReturnsAsync(301);

        var controller = new BookingsController(mockDb.Object, documentsDb: mockDocumentsDb.Object);
        SetCaller(controller, 2);

        var req = new CreateBookingRequest { PakketId = 5, StartDate = DateTime.Today.AddDays(7), EndDate = DateTime.Today.AddDays(12) };
        var result = await controller.CreateBooking(req);

        Assert.IsType<OkObjectResult>(result);
        mockDb.Verify(db => db.CreateBooking(It.IsAny<CreateBookingRequest>(), 2, 5, 0), Times.Once);
    }

    [Fact]
    public async Task CreateBooking_RejectsWhenTargetPjOrganiserHasUnverifiedRequiredDocuments()
    {
        // Same defense-in-depth as the farmer check, but for the package's PJ
        // organiser - Package_db gates browse on both sides, so CreateBooking must too.
        var mockDb = new Mock<Bookings_db>("DummyConnectionString");
        var mockDocumentsDb = new Mock<UserDocuments_db>("DummyConnectionString");
        mockDb.Setup(db => db.GetFarmerUserRoleIdForPackage(5)).ReturnsAsync((int?)null);
        mockDb.Setup(db => db.GetPjUserRoleIdForPackage(5)).ReturnsAsync(77);
        mockDocumentsDb.Setup(db => db.AreRequiredDocumentsApproved(77)).ReturnsAsync(false);

        var controller = new BookingsController(mockDb.Object, documentsDb: mockDocumentsDb.Object);
        SetCaller(controller, 2);

        var req = new CreateBookingRequest { PakketId = 5, StartDate = DateTime.Today.AddDays(7), EndDate = DateTime.Today.AddDays(12) };
        var result = await controller.CreateBooking(req);

        Assert.IsType<BadRequestObjectResult>(result);
        mockDb.Verify(db => db.CreateBooking(It.IsAny<CreateBookingRequest>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<decimal>()), Times.Never);
    }

    [Fact]
    public async Task CreateBooking_SucceedsWhenTargetPjOrganiserIsFullyVerified()
    {
        var mockDb = new Mock<Bookings_db>("DummyConnectionString");
        var mockDocumentsDb = new Mock<UserDocuments_db>("DummyConnectionString");
        mockDb.Setup(db => db.GetFarmerUserRoleIdForPackage(5)).ReturnsAsync((int?)null);
        mockDb.Setup(db => db.GetPjUserRoleIdForPackage(5)).ReturnsAsync(77);
        mockDocumentsDb.Setup(db => db.AreRequiredDocumentsApproved(77)).ReturnsAsync(true);
        mockDb.Setup(db => db.CreateBooking(It.IsAny<CreateBookingRequest>(), 2, 5, 0)).ReturnsAsync(301);

        var controller = new BookingsController(mockDb.Object, documentsDb: mockDocumentsDb.Object);
        SetCaller(controller, 2);

        var req = new CreateBookingRequest { PakketId = 5, StartDate = DateTime.Today.AddDays(7), EndDate = DateTime.Today.AddDays(12) };
        var result = await controller.CreateBooking(req);

        Assert.IsType<OkObjectResult>(result);
        mockDb.Verify(db => db.CreateBooking(It.IsAny<CreateBookingRequest>(), 2, 5, 0), Times.Once);
    }
}

