using System.Net;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Moq;
using WebAPI.Controllers;
using WebAPI.Database;
using WebAPI.Models;
using WebAPI.Services;
using Xunit;

namespace WebAPI.Tests.Controllers;

public class AdminControllerTests
{
    private readonly Mock<AdminAudit_db> _mockAuditDb;
    private readonly Mock<FeatureFlags_db> _mockFlagsDb;
    private readonly Mock<RoleVerifications_db> _mockVerificationsDb;
    private readonly Mock<HealthCheckService> _mockHealthCheckService;
    private readonly Mock<ISessionStore> _mockSessionStore;
    private readonly Mock<Bookings_db> _mockBookingsDb;
    private readonly Mock<User_db> _mockUserDb;
    private readonly Mock<Chats_db> _mockChatsDb;
    private readonly Mock<Rating_db> _mockRatingDb;
    private readonly Mock<SystemSettings_db> _mockSettingsDb;
    private readonly Mock<ISystemSettingsCache> _mockSettingsCache;
    private readonly Mock<UserDocuments_db> _mockDocumentsDb;
    private readonly Mock<IEmailService> _mockEmailService;
    private readonly Mock<Team_db> _mockTeamDb;
    private readonly AdminController _controller;

    public AdminControllerTests()
    {
        _mockAuditDb = new Mock<AdminAudit_db>("DummyConnectionString");
        _mockFlagsDb = new Mock<FeatureFlags_db>("DummyConnectionString");
        _mockVerificationsDb = new Mock<RoleVerifications_db>("DummyConnectionString");
        _mockHealthCheckService = new Mock<HealthCheckService>();
        _mockSessionStore = new Mock<ISessionStore>();
        _mockBookingsDb = new Mock<Bookings_db>("DummyConnectionString");
        _mockUserDb = new Mock<User_db>("DummyConnectionString");
        _mockChatsDb = new Mock<Chats_db>("DummyConnectionString");
        _mockRatingDb = new Mock<Rating_db>("DummyConnectionString");
        _mockSettingsDb = new Mock<SystemSettings_db>("DummyConnectionString");
        _mockSettingsCache = new Mock<ISystemSettingsCache>();
        _mockDocumentsDb = new Mock<UserDocuments_db>("DummyConnectionString");
        _mockEmailService = new Mock<IEmailService>();
        _mockTeamDb = new Mock<Team_db>("DummyConnectionString");

        _mockAuditDb
            .Setup(db => db.InsertAsync(It.IsAny<AdminAuditLogDto>()))
            .ReturnsAsync((AdminAuditLogDto d) => d);

        _controller = new AdminController(_mockAuditDb.Object, _mockFlagsDb.Object, _mockVerificationsDb.Object,
            _mockHealthCheckService.Object, _mockSessionStore.Object,
            _mockBookingsDb.Object, _mockUserDb.Object, _mockChatsDb.Object, _mockRatingDb.Object,
            _mockSettingsDb.Object, _mockSettingsCache.Object, _mockDocumentsDb.Object, _mockEmailService.Object,
            _mockTeamDb.Object);

        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.9");
        httpContext.Request.Headers.UserAgent = "MikPuntTestAgent/1.0";
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "7"),
            new Claim(ClaimTypes.Name, "Johan Admin"),
            new Claim(ClaimTypes.Role, "Admin")
        }, "TestAuth"));

        _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
    }

    [Fact]
    public async Task ToggleFeatureFlag_WithoutReason_ReturnsBadRequest()
    {
        var req = new ToggleFlagRequest { Key = "Enable_Maintenance_Mode", IsEnabled = true, Reason = "" };

        var result = await _controller.ToggleFeatureFlag(req);

        var badRequest = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(400, badRequest.StatusCode);
        _mockFlagsDb.Verify(db => db.ToggleAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ToggleFeatureFlag_WithReason_WritesAuditLogFromClaims_NotBody()
    {
        var flag = new FeatureFlagDto { Key = "Enable_Maintenance_Mode", Description = "d", IsEnabled = true, LastUpdated = DateTime.UtcNow, UpdatedBy = "Johan Admin" };
        _mockFlagsDb.Setup(db => db.ToggleAsync("Enable_Maintenance_Mode", true, "Johan Admin")).ReturnsAsync(flag);

        var req = new ToggleFlagRequest { Key = "Enable_Maintenance_Mode", IsEnabled = true, Reason = "Gepland onderhoud" };

        var result = await _controller.ToggleFeatureFlag(req);

        Assert.IsAssignableFrom<ObjectResult>(result);
        _mockAuditDb.Verify(db => db.InsertAsync(It.Is<AdminAuditLogDto>(d =>
            d.AdminUserId == 7 &&
            d.AdminUserName == "Johan Admin" &&
            d.IpAddress == "203.0.113.9" &&
            d.UserAgent == "MikPuntTestAgent/1.0" &&
            d.Action == "ToggleFeatureFlag")), Times.Once);
    }

    [Fact]
    public async Task DecideRoleVerification_WithoutReason_ReturnsBadRequest()
    {
        var req = new RoleVerificationDecisionRequest { Approve = true, Reason = "" };

        var result = await _controller.DecideRoleVerification(101, req);

        var badRequest = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(400, badRequest.StatusCode);
        _mockVerificationsDb.Verify(db => db.DecideAsync(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task DecideRoleVerification_WhenRequestNotFound_ReturnsNotFound()
    {
        _mockVerificationsDb.Setup(db => db.DecideAsync(999, true, It.IsAny<string>())).ReturnsAsync((RoleVerificationRequestDto?)null);

        var result = await _controller.DecideRoleVerification(999, new RoleVerificationDecisionRequest { Approve = true, Reason = "Geverifieer" });

        var notFound = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(404, notFound.StatusCode);
    }

    [Fact]
    public async Task ExportAuditLogs_ReturnsCsvWithHeaderAndRows()
    {
        _mockAuditDb.Setup(db => db.GetAsync(null, null, null, null, 1, 10000)).ReturnsAsync(new List<AdminAuditLogDto>
        {
            new() { Id = 1, AdminUserId = 7, AdminUserName = "Johan Admin", Action = "ToggleFeatureFlag", TargetEntityType = "FeatureFlag", TargetEntityId = "Enable_Maintenance_Mode", Reason = "Gepland onderhoud, met komma", IpAddress = "203.0.113.9", UserAgent = "MikPuntTestAgent/1.0", Timestamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) }
        });

        var result = await _controller.ExportAuditLogs(null, null, null, null);

        var fileResult = Assert.IsType<FileContentResult>(result);
        Assert.Equal("text/csv", fileResult.ContentType);
        var csv = Encoding.UTF8.GetString(fileResult.FileContents);
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("Id,AdminUserId,AdminUserName,Action,TargetEntityType,TargetEntityId,Reason,IpAddress,UserAgent,TimestampUtc", lines[0].TrimEnd('\r'));
        Assert.Contains("\"Gepland onderhoud, met komma\"", lines[1]);
    }

    [Fact]
    public void Ping_ReturnsOk()
    {
        var result = _controller.Ping();
        var okResult = Assert.IsAssignableFrom<StatusCodeResult>(result);
        Assert.Equal(200, okResult.StatusCode);
    }

    [Fact]
    public async Task CorrectBooking_WithoutReason_ReturnsBadRequest()
    {
        var req = new CorrectBookingRequest { CorrectedTotalPrice = 100m, CorrectedHunterCount = 1, CorrectedObserverCount = 0, Reason = "" };

        var result = await _controller.CorrectBooking(1, req);

        var badRequest = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(400, badRequest.StatusCode);
        _mockBookingsDb.Verify(db => db.UpdateBooking(It.IsAny<int>(), It.IsAny<UpdateBookingRequest>()), Times.Never);
    }

    [Fact]
    public async Task CorrectBooking_WhenBookingNotFound_ReturnsNotFound()
    {
        _mockBookingsDb.Setup(db => db.UpdateBooking(It.IsAny<int>(), It.IsAny<UpdateBookingRequest>())).ReturnsAsync(false);

        var req = new CorrectBookingRequest { CorrectedTotalPrice = 100m, CorrectedHunterCount = 1, CorrectedObserverCount = 0, Reason = "Prys was verkeerd" };
        var result = await _controller.CorrectBooking(999, req);

        var notFound = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(404, notFound.StatusCode);
    }

    [Fact]
    public async Task UpdateUserStatus_WithoutReason_ReturnsBadRequest()
    {
        var req = new UpdateUserStatusRequest { Status = "Suspended", Reason = "" };

        var result = await _controller.UpdateUserStatus(5, req);

        var badRequest = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(400, badRequest.StatusCode);
        _mockUserDb.Verify(db => db.UpdateAccountStatus(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task UpdateUserStatus_WithReason_WritesAuditLog()
    {
        _mockUserDb.Setup(db => db.UpdateAccountStatus(5, "Suspended", "Wangedrag")).ReturnsAsync(true);

        var req = new UpdateUserStatusRequest { Status = "Suspended", Reason = "Wangedrag" };
        var result = await _controller.UpdateUserStatus(5, req);

        Assert.IsAssignableFrom<ObjectResult>(result);
        _mockAuditDb.Verify(db => db.InsertAsync(It.Is<AdminAuditLogDto>(d => d.Action == "SuspendUser" && d.TargetEntityId == "5")), Times.Once);
    }

    [Theory]
    [InlineData("Active")]
    [InlineData("Approved")]   // what the old Aktiveer button sent
    public async Task UpdateUserStatus_Reactivating_StoresActive(string sent)
    {
        _mockUserDb.Setup(db => db.UpdateAccountStatus(5, "Active", "Hersien")).ReturnsAsync(true);

        await _controller.UpdateUserStatus(5, new UpdateUserStatusRequest { Status = sent, Reason = "Hersien" });

        _mockUserDb.Verify(db => db.UpdateAccountStatus(5, "Active", "Hersien"), Times.Once);
        _mockUserDb.Verify(db => db.DeleteAppSessionsAsync(It.IsAny<int>()), Times.Never);
        _mockAuditDb.Verify(db => db.InsertAsync(It.Is<AdminAuditLogDto>(d => d.Action == "ReactivateUser")), Times.Once);
    }

    [Fact]
    public async Task UpdateUserStatus_Suspending_EndsTheAppSessions()
    {
        _mockUserDb.Setup(db => db.UpdateAccountStatus(5, "Suspended", "Wangedrag")).ReturnsAsync(true);

        await _controller.UpdateUserStatus(5, new UpdateUserStatusRequest { Status = "Suspended", Reason = "Wangedrag" });

        _mockUserDb.Verify(db => db.DeleteAppSessionsAsync(5), Times.Once);
    }

    [Fact]
    public async Task UpdateUserStatus_UnknownStatus_ReturnsBadRequest()
    {
        var result = await _controller.UpdateUserStatus(5, new UpdateUserStatusRequest { Status = "Whatever", Reason = "x" });

        Assert.IsType<BadRequestObjectResult>(result);
        _mockUserDb.Verify(db => db.UpdateAccountStatus(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ModerateFlaggedMessage_WhenNotFound_ReturnsNotFound()
    {
        _mockChatsDb.Setup(db => db.ModerateMessage(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>())).ReturnsAsync(false);

        var result = await _controller.ModerateFlaggedMessage(1, new ModerateMessageRequest { Action = "Dismissed", Reason = "Vals positief" });

        var notFound = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(404, notFound.StatusCode);
    }

    [Fact]
    public async Task ResolveRatingDispute_WithInvalidStatus_ReturnsBadRequest()
    {
        var result = await _controller.ResolveRatingDispute(1, new ResolveDisputeRequest { Status = "Maybe", Notes = "" });

        Assert.IsType<BadRequestObjectResult>(result);
        _mockRatingDb.Verify(db => db.ResolveDispute(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ResolveRatingDispute_WhenNotFound_ReturnsNotFound()
    {
        _mockRatingDb.Setup(db => db.ResolveDispute(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>())).ReturnsAsync(false);

        var result = await _controller.ResolveRatingDispute(1, new ResolveDisputeRequest { Status = "Upheld", Notes = "Confirmed unfair" });

        var notFound = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(404, notFound.StatusCode);
    }

    [Fact]
    public async Task ResolveRatingDispute_WhenUpheld_WritesAuditLogAndReturnsOk()
    {
        _mockRatingDb.Setup(db => db.ResolveDispute(1, "Upheld", 7, "Confirmed unfair")).ReturnsAsync(true);

        var result = await _controller.ResolveRatingDispute(1, new ResolveDisputeRequest { Status = "Upheld", Notes = "Confirmed unfair" });

        Assert.IsType<OkObjectResult>(result);
        _mockAuditDb.Verify(db => db.InsertAsync(It.Is<AdminAuditLogDto>(d => d.Action == "ResolveRatingDispute_Upheld" && d.TargetEntityId == "1")), Times.Once);
    }

    [Fact]
    public async Task GetRatingDisputes_ReturnsListFromDb()
    {
        var disputes = new List<RatingDisputeDto> { new() { DisputeId = 1, RatingId = 42, Status = "PendingReview" } };
        _mockRatingDb.Setup(db => db.GetDisputes(null)).ReturnsAsync(disputes);

        var result = await _controller.GetRatingDisputes();

        var okResult = Assert.IsType<OkObjectResult>(result);
        var list = Assert.IsType<List<RatingDisputeDto>>(okResult.Value);
        Assert.Single(list);
    }

    [Fact]
    public async Task UpdateSetting_WithoutReason_ReturnsBadRequest()
    {
        var req = new UpdateSettingRequest { Key = "AdMonthlyPrice", Value = "150", Reason = "" };

        var result = await _controller.UpdateSetting(req);

        var badRequest = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(400, badRequest.StatusCode);
        _mockSettingsDb.Verify(db => db.SetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [InlineData("not-a-number")]
    [InlineData("-5")]
    public async Task UpdateSetting_WithInvalidValue_ReturnsBadRequest(string value)
    {
        var req = new UpdateSettingRequest { Key = "AdMonthlyPrice", Value = value, Reason = "Toets" };

        var result = await _controller.UpdateSetting(req);

        var badRequest = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(400, badRequest.StatusCode);
        _mockSettingsDb.Verify(db => db.SetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task UpdateSetting_WithValidValue_UpdatesCacheAndWritesAuditLog()
    {
        var updated = new SystemSettingDto { Key = "AdMonthlyPrice", Value = "150", Description = "d", IsPublic = true, UpdatedBy = "Johan Admin" };
        _mockSettingsDb.Setup(db => db.SetAsync("AdMonthlyPrice", "150", "Johan Admin")).ReturnsAsync(updated);

        var req = new UpdateSettingRequest { Key = "AdMonthlyPrice", Value = "150", Reason = "Prysverhoging" };
        var result = await _controller.UpdateSetting(req);

        Assert.IsAssignableFrom<ObjectResult>(result);
        _mockSettingsCache.Verify(c => c.Set("AdMonthlyPrice", "150"), Times.Once);
        _mockAuditDb.Verify(db => db.InsertAsync(It.Is<AdminAuditLogDto>(d => d.Action == "UpdateSystemSetting" && d.TargetEntityId == "AdMonthlyPrice")), Times.Once);
    }

    [Fact]
    public async Task GetDocumentVerifications_PassesStatusAndRoleCodeThroughAndReturnsListFromDb()
    {
        var queue = new List<PendingDocumentVerificationDto> { new() { Id = 1, DocumentTypeName = "Besigheidsregistrasie", RoleCode = "PJ" } };
        _mockDocumentsDb.Setup(db => db.GetPendingVerificationQueue("Pending", "PJ")).ReturnsAsync(queue);

        var result = await _controller.GetDocumentVerifications("Pending", "PJ");

        var okResult = Assert.IsType<OkObjectResult>(result);
        var list = Assert.IsType<List<PendingDocumentVerificationDto>>(okResult.Value);
        Assert.Single(list);
    }

    [Fact]
    public async Task DecideDocumentVerification_RejectingWithoutReason_ReturnsBadRequest()
    {
        var req = new DocumentVerificationDecisionRequest { Approve = false, Reason = "" };

        var result = await _controller.DecideDocumentVerification(1, req);

        var badRequest = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(400, badRequest.StatusCode);
        _mockDocumentsDb.Verify(db => db.VerifyDocument(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task DecideDocumentVerification_WhenAlreadyDecided_ReturnsNotFound()
    {
        _mockDocumentsDb.Setup(db => db.VerifyDocument(999, 7, true, null)).ReturnsAsync((DocumentVerificationResultDto?)null);

        var result = await _controller.DecideDocumentVerification(999, new DocumentVerificationDecisionRequest { Approve = true });

        var notFound = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(404, notFound.StatusCode);
    }

    [Fact]
    public async Task DecideDocumentVerification_WhenApproved_WritesAuditLogEmailsOwnerAndNotifiesInApp()
    {
        var decided = new DocumentVerificationResultDto
        {
            Id = 1,
            Status = "Approved",
            OwnerUserId = 55,
            OwnerEmail = "eienaar@test.local",
            OwnerName = "Piet Eienaar",
            RoleCode = "PH",
            RoleName = "Professionele Jagter",
            DocumentTypeName = "Vuurwapenbevoegdheidslisensie"
        };
        _mockDocumentsDb.Setup(db => db.VerifyDocument(1, 7, true, null)).ReturnsAsync(decided);

        var result = await _controller.DecideDocumentVerification(1, new DocumentVerificationDecisionRequest { Approve = true });

        Assert.IsAssignableFrom<ObjectResult>(result);
        _mockAuditDb.Verify(db => db.InsertAsync(It.Is<AdminAuditLogDto>(d =>
            d.Action == "ApproveDocument" && d.TargetEntityType == "UserDocument" && d.TargetEntityId == "1")), Times.Once);
        _mockEmailService.Verify(e => e.SendEmailAsync("eienaar@test.local", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Once);
        _mockTeamDb.Verify(db => db.InsertNotification(55, 7, "DOCUMENT_APPROVED", It.IsAny<string>(), It.IsAny<string>(), "/ph/paneelbord"), Times.Once);
    }

    [Fact]
    public async Task DecideDocumentVerification_WhenRejected_WritesAuditLogEmailsOwnerAndNotifiesInApp()
    {
        var decided = new DocumentVerificationResultDto
        {
            Id = 2,
            Status = "Rejected",
            OwnerUserId = 55,
            OwnerEmail = "eienaar@test.local",
            OwnerName = "Piet Eienaar",
            RoleCode = "FARMER",
            RoleName = "Boer/Grondeienaar",
            DocumentTypeName = "Vuurwapenbevoegdheidslisensie",
            RejectionReason = "Dokument is onduidelik"
        };
        _mockDocumentsDb.Setup(db => db.VerifyDocument(2, 7, false, "Dokument is onduidelik")).ReturnsAsync(decided);

        var req = new DocumentVerificationDecisionRequest { Approve = false, Reason = "Dokument is onduidelik" };
        var result = await _controller.DecideDocumentVerification(2, req);

        Assert.IsAssignableFrom<ObjectResult>(result);
        _mockAuditDb.Verify(db => db.InsertAsync(It.Is<AdminAuditLogDto>(d =>
            d.Action == "RejectDocument" && d.TargetEntityId == "2")), Times.Once);
        _mockEmailService.Verify(e => e.SendEmailAsync("eienaar@test.local", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Once);
        _mockTeamDb.Verify(db => db.InsertNotification(55, 7, "DOCUMENT_REJECTED",
            It.IsAny<string>(), It.Is<string>(m => m.Contains("Dokument is onduidelik")), "/boer/paneelbord"), Times.Once);
    }

    [Fact]
    public async Task DecideDocumentVerification_WhenEmailSendThrows_StillReturnsOkAndStillNotifiesInApp()
    {
        // Best-effort notification: the decision itself already succeeded and was
        // audited, so a broken mail server must never fail the whole request, and
        // the in-app notification (a separate channel) must still go out.
        var decided = new DocumentVerificationResultDto
        {
            Id = 3,
            Status = "Approved",
            OwnerUserId = 55,
            OwnerEmail = "eienaar@test.local",
            OwnerName = "Piet Eienaar",
            RoleCode = "PH",
            RoleName = "Professionele Jagter",
            DocumentTypeName = "Vuurwapenbevoegdheidslisensie"
        };
        _mockDocumentsDb.Setup(db => db.VerifyDocument(3, 7, true, null)).ReturnsAsync(decided);
        _mockEmailService.Setup(e => e.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
            .ThrowsAsync(new InvalidOperationException("SMTP down"));

        var result = await _controller.DecideDocumentVerification(3, new DocumentVerificationDecisionRequest { Approve = true });

        Assert.IsType<OkObjectResult>(result);
        _mockTeamDb.Verify(db => db.InsertNotification(55, 7, "DOCUMENT_APPROVED", It.IsAny<string>(), It.IsAny<string>(), "/ph/paneelbord"), Times.Once);
    }

    [Fact]
    public async Task DecideRoleVerification_WhenApproved_EmailsAndNotifiesUser()
    {
        var updated = new RoleVerificationRequestDto
        {
            Id = 10,
            UserId = 42,
            UserName = "Piet Professioneel",
            UserEmail = "piet@test.local",
            RequestedRoleCode = "PH",
            RequestedRoleName = "Professionele Jagter"
        };
        _mockVerificationsDb.Setup(db => db.DecideAsync(10, true, "Alles in orde")).ReturnsAsync(updated);

        var result = await _controller.DecideRoleVerification(10, new RoleVerificationDecisionRequest { Approve = true, Reason = "Alles in orde" });

        Assert.IsAssignableFrom<ObjectResult>(result);
        _mockEmailService.Verify(e => e.SendEmailAsync("piet@test.local", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Once);
        _mockTeamDb.Verify(db => db.InsertNotification(42, 7, "ROLE_APPROVED", It.IsAny<string>(), It.IsAny<string>(), "/ph/paneelbord"), Times.Once);
    }

    [Fact]
    public async Task DecideRoleVerification_WhenRejected_EmailsAndNotifiesUserWithReason()
    {
        var updated = new RoleVerificationRequestDto
        {
            Id = 11,
            UserId = 43,
            UserName = "Bennie Boer",
            UserEmail = "bennie@test.local",
            RequestedRoleCode = "FARMER",
            RequestedRoleName = "Boer/Grondeienaar"
        };
        _mockVerificationsDb.Setup(db => db.DecideAsync(11, false, "Onvolledige dokumentasie")).ReturnsAsync(updated);

        var req = new RoleVerificationDecisionRequest { Approve = false, Reason = "Onvolledige dokumentasie" };
        var result = await _controller.DecideRoleVerification(11, req);

        Assert.IsAssignableFrom<ObjectResult>(result);
        _mockEmailService.Verify(e => e.SendEmailAsync("bennie@test.local", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Once);
        _mockTeamDb.Verify(db => db.InsertNotification(43, 7, "ROLE_REJECTED",
            It.IsAny<string>(), It.Is<string>(m => m.Contains("Onvolledige dokumentasie")), "/boer/paneelbord"), Times.Once);
    }
}
