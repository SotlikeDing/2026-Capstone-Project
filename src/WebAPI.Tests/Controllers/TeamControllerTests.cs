using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Moq;
using WebAPI.Controllers;
using WebAPI.Database;
using WebAPI.Models;
using Xunit;

namespace WebAPI.Tests.Controllers;

public class TeamControllerTests
{
    private readonly Mock<Team_db> _mockDb;
    private readonly Mock<ResourceOwnership_db> _mockOwnership;
    private readonly TeamController _controller;

    /// <summary>The default caller: the PJ (userId 100) most of these tests act as.</summary>
    private const int PjUserId = 100;

    public TeamControllerTests()
    {
        _mockDb = new Mock<Team_db>("DummyConnectionString");
        _mockOwnership = new Mock<ResourceOwnership_db>("DummyConnectionString");
        _mockOwnership.Setup(o => o.GetUserIdForUserRole(20)).ReturnsAsync(PjUserId);                     // member role 20
        _mockOwnership.Setup(o => o.GetInviteSenderUserIds(1)).ReturnsAsync(new HashSet<int> { PjUserId });  // invite 1
        _mockOwnership.Setup(o => o.GetInviteRecipientUserIds(1)).ReturnsAsync(new HashSet<int> { PjUserId });
        _mockOwnership.Setup(o => o.GetTeamOwnerUserIds(5)).ReturnsAsync(new HashSet<int> { PjUserId });     // team row 5
        _mockOwnership.Setup(o => o.GetInviteSenderUserIds(2)).ReturnsAsync(new HashSet<int> { 777 });       // someone else's invite
        _controller = new TeamController(_mockDb.Object, ownership: _mockOwnership.Object);
        SetCaller(PjUserId);
    }

    [Fact]
    public async Task SendInvite_OnBehalfOfAnotherPj_ReturnsForbid()
    {
        var result = await _controller.SendInvite(new SendInviteRequest { PjUserId = 555, MemberUserRoleId = 10 });

        Assert.IsType<ForbidResult>(result);
        _mockDb.Verify(db => db.SendInvite(
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task CancelInvite_SentBySomeoneElse_ReturnsForbid()
    {
        var result = await _controller.CancelInvite(2);

        Assert.IsType<ForbidResult>(result);
        _mockDb.Verify(db => db.CancelInvite(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task GetInvitesForUser_ForAnotherUser_ReturnsForbid()
    {
        var result = await _controller.GetInvitesForUser(PjUserId + 1);

        Assert.IsType<ForbidResult>(result);
    }

    private void SetCaller(int userId, bool isAdmin = false) => SetCaller(_controller, userId, isAdmin);

    /// <summary>Clears the default signed-in caller set up in the constructor.</summary>
    private void SetAnonymousCaller()
        => _controller.ControllerContext = new ControllerContext { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() };

    private static void SetCaller(TeamController controller, int userId, bool isAdmin = false)
    {
        var claims = new System.Collections.Generic.List<System.Security.Claims.Claim>
        {
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, userId.ToString()),
            new System.Security.Claims.Claim("userId", userId.ToString())
        };

        if (isAdmin)
        {
            claims.Add(new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, "Admin"));
        }

        var identity = new System.Security.Claims.ClaimsIdentity(claims, "TestAuth");
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
            {
                User = new System.Security.Claims.ClaimsPrincipal(identity)
            }
        };
    }

    [Fact]
    public async Task SendInvite_WithInvalidPjUserId_ReturnsBadRequest()
    {
        // Arrange
        var request = new SendInviteRequest { PjUserId = 0, MemberUserRoleId = 10 };

        // Act
        var result = await _controller.SendInvite(request);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(badRequestResult.Value);
        Assert.Contains("PjUserId is missing or invalid", badRequestResult.Value.ToString());
    }

    [Fact]
    public async Task SendInvite_WhenTargetIsSelf_ReturnsBadRequest()
    {
        // Arrange
        var request = new SendInviteRequest { PjUserId = 100, TargetUserId = 100, MemberUserRoleId = 10 };

        // Act
        var result = await _controller.SendInvite(request);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(badRequestResult.Value);
        Assert.Contains("U kan nie uself as 'n spanlid uitnooi nie", badRequestResult.Value.ToString());
    }

    [Fact]
    public async Task SendInvite_WhenUserIsNotPj_ReturnsBadRequest()
    {
        // Arrange
        var request = new SendInviteRequest { PjUserId = 100, MemberUserRoleId = 10 };
        _mockDb.Setup(db => db.GetUserRoleId(100, "PJ")).ReturnsAsync(0);

        // Act
        var result = await _controller.SendInvite(request);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(badRequestResult.Value);
        Assert.Contains("User is not registered as an active PJ", badRequestResult.Value.ToString());
    }

    [Fact]
    public async Task SendInvite_WithValidData_ReturnsOkWithInviteId()
    {
        // Arrange
        var request = new SendInviteRequest
        {
            PjUserId = 100,
            MemberUserRoleId = 10,
            ExpeditionName = "Waterberg-span",
            Terms = "Vier dae; verblyf en gids ingesluit."
        };
        _mockDb.Setup(db => db.GetUserRoleId(100, "PJ")).ReturnsAsync(50); // PjUserRoleId
        _mockDb.Setup(db => db.SendInvite(50, 10, "Waterberg-span", "Vier dae; verblyf en gids ingesluit."))
            .ReturnsAsync(999); // InviteId

        // Act
        var result = await _controller.SendInvite(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
        Assert.Contains("999", okResult.Value.ToString());
        _mockDb.Verify(db => db.SendInvite(
            50, 10, "Waterberg-span", "Vier dae; verblyf en gids ingesluit."), Times.Once);
    }

    [Fact]
    public async Task SendInvite_WithOverlongTerms_ReturnsBadRequest()
    {
        var request = new SendInviteRequest
        {
            PjUserId = 100,
            MemberUserRoleId = 10,
            Terms = new string('x', 1001)
        };

        var result = await _controller.SendInvite(request);

        Assert.IsType<BadRequestObjectResult>(result);
        _mockDb.Verify(db => db.SendInvite(
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task GetTeamsForMember_ReturnsOkWithData()
    {
        // Arrange
        var memberId = 20;
        var teams = new List<TeamMemberResponse>
        {
            new TeamMemberResponse { Id = 1, MemberUserRoleId = 50, UserId = 100, Name = "John", Surname = "Doe", Role = "PJ" }
        };
        _mockDb.Setup(db => db.GetTeamsForMember(memberId)).ReturnsAsync(teams);

        // Act
        var result = await _controller.GetTeamsForMember(memberId);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var responseList = Assert.IsType<List<TeamMemberResponse>>(okResult.Value);
        Assert.Single(responseList);
        Assert.Equal(50, responseList[0].MemberUserRoleId);
    }

    [Fact]
    public async Task RespondInvite_WhenSuccess_ReturnsOk()
    {
        // Arrange
        _mockDb.Setup(db => db.RespondToInvite(1, true)).ReturnsAsync(true);

        // Act
        var result = await _controller.RespondInvite(1, true);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
        Assert.Contains("True", okResult.Value.ToString() ?? "");
    }

    [Fact]
    public async Task RespondInvite_WhenFails_ReturnsNotFound()
    {
        // Arrange
        _mockDb.Setup(db => db.RespondToInvite(1, true)).ReturnsAsync(false);

        // Act
        var result = await _controller.RespondInvite(1, true);

        // Assert
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Browse_ReturnsOkWithBrowseProfiles()
    {
        // Arrange
        var profiles = new List<BrowseProfileResponse>
        {
            new BrowseProfileResponse
            {
                UserId = 10,
                UserRoleId = 25,
                Name = "Hendrik",
                Surname = "Viljoen",
                RoleCode = "PH",
                BusinessName = "Viljoen Safaris",
                Bio = "Dangerous game PH",
                YearsExperience = 12,
                Phone = "0821234567"
            }
        };
        _mockDb.Setup(db => db.BrowseByRoles(It.IsAny<List<string>>())).ReturnsAsync(profiles);

        // Act
        var result = await _controller.Browse("PH,VG");

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var list = Assert.IsType<List<BrowseProfileResponse>>(okResult.Value);
        Assert.Single(list);
        Assert.Equal("Hendrik", list[0].Name);
        Assert.Equal("Viljoen Safaris", list[0].BusinessName);
    }

    [Fact]
    public async Task GetInvitesForUser_ReturnsOkWithInvites()
    {
        // Arrange
        var invites = new List<TeamInviteResponse>
        {
            new TeamInviteResponse
            {
                InviteId = 1,
                PjUserId = 5,
                PjName = "Kobus",
                TargetUserId = 10,
                Status = "Pending"
            }
        };
        _mockDb.Setup(db => db.GetInvitesForUser(10)).ReturnsAsync(invites);
        SetCaller(10); // a user reads their own invites

        // Act
        var result = await _controller.GetInvitesForUser(10);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var list = Assert.IsType<List<TeamInviteResponse>>(okResult.Value);
        Assert.Single(list);
        Assert.Equal("Kobus", list[0].PjName);
    }

    [Fact]
    public async Task CancelInvite_WhenFound_ReturnsOk()
    {
        // Arrange
        _mockDb.Setup(db => db.CancelInvite(1)).ReturnsAsync(true);

        // Act
        var result = await _controller.CancelInvite(1);

        // Assert
        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task CheckRemovalConflicts_ReturnsOkWithConflictResponse()
    {
        // Arrange
        var expected = new RemovalConflictCheckResponse
        {
            TeamId = 5,
            MemberUserId = 12,
            MemberName = "Jan Boer",
            CanRemoveDirectly = true,
            Message = "Lid kan direk verwyder word."
        };
        _mockDb.Setup(db => db.CheckMemberRemovalConflicts(5)).ReturnsAsync(expected);

        // Act
        var result = await _controller.CheckRemovalConflicts(5);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var value = Assert.IsType<RemovalConflictCheckResponse>(okResult.Value);
        Assert.Equal(5, value.TeamId);
        Assert.True(value.CanRemoveDirectly);
    }

    [Fact]
    public void GetRemovalReasons_ReturnsOkWithList()
    {
        // Arrange
        var reasons = new List<RemovalReasonOptionDto>
        {
            new() { Code = "MUTUAL_AGREEMENT", Description = "Mutual agreement" }
        };
        _mockDb.Setup(db => db.GetRemovalReasons()).Returns(reasons);

        // Act
        var result = _controller.GetRemovalReasons();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var list = Assert.IsType<List<RemovalReasonOptionDto>>(okResult.Value);
        Assert.Single(list);
        Assert.Equal("MUTUAL_AGREEMENT", list[0].Code);
    }

    [Fact]
    public async Task RemoveMember_WhenCallerHasNoIdentity_ReturnsUnauthorized_EvenWithPjUserIdInBody()
    {
        SetAnonymousCaller();
        // request.PjUserId used to be trusted as the caller's identity whenever there
        // was no authenticated session at all - since ExecuteTeamMemberRemoval treats
        // that value as the real team-ownership check, an unauthenticated request could
        // remove any PJ's team member just by guessing/knowing that PJ's userId.
        var req = new RemoveTeamMemberRequest { TeamId = 7, ReasonCode = "OTHER", PjUserId = 1 };

        var result = await _controller.RemoveMember(req);

        Assert.IsType<UnauthorizedResult>(result);
        _mockDb.Verify(db => db.ExecuteTeamMemberRemoval(It.IsAny<int>(), It.IsAny<RemoveTeamMemberRequest>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task RemoveMember_WithInvalidTeamId_ReturnsBadRequest()
    {
        // Arrange
        var req = new RemoveTeamMemberRequest { TeamId = 0 };

        // Act
        var result = await _controller.RemoveMember(req);

        // Assert
        var badReq = Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(badReq.Value);
        Assert.Contains("Invalid TeamId", badReq.Value.ToString());
    }

    [Fact]
    public async Task RemoveMember_WhenActiveBookingConflict_ReturnsBadRequest400()
    {
        // Arrange
        SetCaller(1);
        var req = new RemoveTeamMemberRequest { TeamId = 7, ReasonCode = "OTHER" };
        _mockDb.Setup(db => db.ExecuteTeamMemberRemoval(7, req, It.IsAny<int>()))
               .ThrowsAsync(new InvalidOperationException("ACTIVE_BOOKING_CONFLICT"));

        // Act
        var result = await _controller.RemoveMember(req);

        // Assert
        var badReq = Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(badReq.Value);
        Assert.Contains("ACTIVE_BOOKING_CONFLICT", badReq.Value.ToString());
    }

    [Fact]
    public async Task RemoveMember_WhenUpcomingBookingsRequireConfirmation_ReturnsConflict409()
    {
        // Arrange
        SetCaller(1);
        var req = new RemoveTeamMemberRequest { TeamId = 8, ReasonCode = "OTHER", ConfirmReassignment = false };
        _mockDb.Setup(db => db.ExecuteTeamMemberRemoval(8, req, It.IsAny<int>()))
               .ThrowsAsync(new InvalidOperationException("UPCOMING_BOOKINGS_REQUIRE_CONFIRMATION"));

        // Act
        var result = await _controller.RemoveMember(req);

        // Assert
        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(409, objResult.StatusCode);
        Assert.NotNull(objResult.Value);
        Assert.Contains("UPCOMING_BOOKINGS_REQUIRE_CONFIRMATION", objResult.Value.ToString());
    }

    [Fact]
    public async Task RemoveMember_WhenSuccess_DispatchesChatAndEmailAndReturnsOk()
    {
        // Arrange
        var mockChats = new Mock<Chats_db>("DummyConn");
        var mockEmail = new Mock<WebAPI.Services.IEmailService>();
        var mockHub = new Mock<IHubContext<WebAPI.Hubs.ChatHub>>();
        var mockClients = new Mock<IHubClients>();
        var mockClientProxy = new Mock<IClientProxy>();

        mockHub.Setup(h => h.Clients).Returns(mockClients.Object);
        mockClients.Setup(c => c.Group(It.IsAny<string>())).Returns(mockClientProxy.Object);
        mockChats.Setup(c => c.GetOrCreateDirectChat(It.IsAny<int>(), 20, It.IsAny<string>())).ReturnsAsync(123);

        var controller = new TeamController(_mockDb.Object, mockChats.Object, mockEmail.Object, mockHub.Object);
        SetCaller(controller, 1);

        var req = new RemoveTeamMemberRequest { TeamId = 9, ReasonCode = "MUTUAL_AGREEMENT", ReasonDescription = "Agreement", PjUserId = 1 };
        var removalResult = new TeamRemovalResult
        {
            Success = true,
            TeamId = 9,
            RemovedMemberUserId = 20,
            RemovedMemberName = "Piet Skiet",
            RemovedMemberEmail = "piet@test.com",
            UnassignedBookingCount = 2,
            UnassignedBookingIds = new List<int> { 101, 102 },
            Message = "Success"
        };

        _mockDb.Setup(db => db.ExecuteTeamMemberRemoval(9, req, 1)).ReturnsAsync(removalResult);

        // Act
        var result = await controller.RemoveMember(req);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var value = Assert.IsType<TeamRemovalResult>(okResult.Value);
        Assert.True(value.Success);
        Assert.Equal(2, value.UnassignedBookingCount);

        mockChats.Verify(c => c.GetOrCreateDirectChat(1, 20, It.IsAny<string>()), Times.Once);
        mockEmail.Verify(e => e.SendEmailAsync("piet@test.com", It.IsAny<string>(), It.IsAny<string>(), true), Times.Once);
    }

    [Fact]
    public async Task GetUserNotifications_AnonymousCaller_ReturnsUnauthorized()
    {
        SetAnonymousCaller();
        // No SetCaller - this is the exact shape of the fix: an unauthenticated
        // request must never fall through to "not the same user, but allow anyway"
        // just because TryGetCurrentUserId returned false.
        var result = await _controller.GetUserNotifications(10, 1, 20);

        Assert.IsType<UnauthorizedResult>(result);
        _mockDb.Verify(db => db.GetUserNotifications(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task GetUserNotifications_DifferentAuthenticatedUser_ReturnsForbidden()
    {
        SetCaller(11);

        var result = await _controller.GetUserNotifications(10, 1, 20);

        var forbidden = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(403, forbidden.StatusCode);
        _mockDb.Verify(db => db.GetUserNotifications(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task GetUserNotifications_Owner_ReturnsOk()
    {
        SetCaller(10);
        var notifications = new List<UserNotificationDto>
        {
            new() { NotificationId = 1, Title = "Test Title", Message = "Test Message", IsRead = false }
        };
        _mockDb.Setup(db => db.GetUserNotifications(10, 1, 20)).ReturnsAsync(notifications);

        var result = await _controller.GetUserNotifications(10, 1, 20);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var list = Assert.IsType<List<UserNotificationDto>>(okResult.Value);
        Assert.Single(list);
        Assert.Equal("Test Title", list[0].Title);
    }

    [Fact]
    public async Task GetUserNotifications_Admin_CanViewAnotherUsersNotifications()
    {
        SetCaller(999, isAdmin: true);
        _mockDb.Setup(db => db.GetUserNotifications(10, 1, 20)).ReturnsAsync(new List<UserNotificationDto>());

        var result = await _controller.GetUserNotifications(10, 1, 20);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task GetUnreadNotificationCount_AnonymousCaller_ReturnsUnauthorized()
    {
        SetAnonymousCaller();
        var result = await _controller.GetUnreadNotificationCount(10);

        Assert.IsType<UnauthorizedResult>(result);
        _mockDb.Verify(db => db.GetUnreadNotificationCount(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task GetUnreadNotificationCount_Owner_ReturnsCount()
    {
        SetCaller(10);
        _mockDb.Setup(db => db.GetUnreadNotificationCount(10)).ReturnsAsync(4);

        var result = await _controller.GetUnreadNotificationCount(10);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
        Assert.Contains("4", okResult.Value.ToString());
    }

    [Fact]
    public async Task MarkNotificationAsRead_AnonymousCaller_ReturnsUnauthorized()
    {
        SetAnonymousCaller();
        var result = await _controller.MarkNotificationAsRead(99);

        Assert.IsType<UnauthorizedResult>(result);
        _mockDb.Verify(db => db.MarkNotificationAsRead(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task MarkNotificationAsRead_WhenFound_ReturnsOk()
    {
        SetCaller(7);
        _mockDb.Setup(db => db.MarkNotificationAsRead(99, 7, false)).ReturnsAsync(true);

        var result = await _controller.MarkNotificationAsRead(99);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
        Assert.Contains("True", okResult.Value.ToString());
    }

    [Fact]
    public async Task MarkNotificationAsRead_WhenNotFoundOrNotOwned_ReturnsNotFound()
    {
        // The DB layer returns false both for a nonexistent id and for a
        // notification that belongs to someone else - same "not found" contract,
        // never leaking whether the id exists to a non-owner.
        SetCaller(7);
        _mockDb.Setup(db => db.MarkNotificationAsRead(99, 7, false)).ReturnsAsync(false);

        var result = await _controller.MarkNotificationAsRead(99);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task MarkNotificationAsRead_Admin_PassesIsAdminThrough()
    {
        SetCaller(999, isAdmin: true);
        _mockDb.Setup(db => db.MarkNotificationAsRead(99, 999, true)).ReturnsAsync(true);

        var result = await _controller.MarkNotificationAsRead(99);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task GetUserNotifications_WhenAccessingOtherUserNotifications_ReturnsForbidden403()
    {
        // Arrange
        SetCaller(5, isAdmin: false);

        // Act
        var result = await _controller.GetUserNotifications(10);

        // Assert
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, objectResult.StatusCode);
    }

    [Fact]
    public async Task GetUnreadNotificationCount_WhenAccessingOtherUserNotifications_ReturnsForbidden403()
    {
        // Arrange
        SetCaller(5, isAdmin: false);

        // Act
        var result = await _controller.GetUnreadNotificationCount(10);

        // Assert
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, objectResult.StatusCode);
    }

    [Fact]
    public async Task GetUserNotifications_WhenAdminAccessesOtherUserNotifications_ReturnsOk()
    {
        // Arrange
        SetCaller(5, isAdmin: true);
        _mockDb.Setup(db => db.GetUserNotifications(10, 1, 20)).ReturnsAsync(new List<UserNotificationDto>());

        // Act
        var result = await _controller.GetUserNotifications(10);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public async Task GetUnreadNotificationCount_WhenAdminAccessesOtherUserNotifications_ReturnsOk()
    {
        // Arrange
        SetCaller(5, isAdmin: true);
        _mockDb.Setup(db => db.GetUnreadNotificationCount(10)).ReturnsAsync(3);

        // Act
        var result = await _controller.GetUnreadNotificationCount(10);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
        Assert.Contains("3", okResult.Value.ToString());
    }
}

