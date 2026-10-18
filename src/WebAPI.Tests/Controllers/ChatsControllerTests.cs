using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Moq;
using WebAPI.Controllers;
using WebAPI.Database;
using WebAPI.Hubs;
using WebAPI.Models;
using Xunit;

namespace WebAPI.Tests.Controllers;

public class ChatsControllerTests
{
    private readonly Mock<Chats_db> _mockDb;
    private readonly Mock<IHubContext<ChatHub>> _mockHubContext;
    private readonly Mock<IHubClients> _mockClients;
    private readonly Mock<IClientProxy> _mockClientProxy;
    private readonly ChatsController _controller;

    public ChatsControllerTests()
    {
        _mockDb = new Mock<Chats_db>("DummyConnectionString");
        _mockHubContext = new Mock<IHubContext<ChatHub>>();
        _mockClients = new Mock<IHubClients>();
        _mockClientProxy = new Mock<IClientProxy>();

        _mockHubContext.Setup(h => h.Clients).Returns(_mockClients.Object);
        _mockClients.Setup(c => c.Group(It.IsAny<string>())).Returns(_mockClientProxy.Object);
        _mockClientProxy
            .Setup(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), default))
            .Returns(Task.CompletedTask);

        _mockDb.Setup(db => db.IsParticipant(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(true);

        _controller = new ChatsController(_mockDb.Object, _mockHubContext.Object);
    }

    private void SetCaller(int userId, bool isAdmin = false)
    {
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim("userId", userId.ToString())
        };

        if (isAdmin)
        {
            claims.Add(new Claim(ClaimTypes.Role, "Admin"));
        }

        var identity = new ClaimsIdentity(claims, "TestAuth");
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity)
            }
        };
    }

    private static void AssertStatusCode(IActionResult result, int expectedStatusCode)
    {
        if (result is ObjectResult obj)
        {
            Assert.Equal(expectedStatusCode, obj.StatusCode);
        }
        else if (result is StatusCodeResult scr)
        {
            Assert.Equal(expectedStatusCode, scr.StatusCode);
        }
        else if (result is ForbidResult && expectedStatusCode == 403)
        {
            Assert.True(true);
        }
        else
        {
            Assert.Fail($"Expected HTTP status code {expectedStatusCode}, but got {result.GetType().Name}");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 1. GetUserChats Tests
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetUserChats_WhenAuthorized_ReturnsOkWithListOfChats()
    {
        // Arrange
        int userId = 1;
        SetCaller(userId);

        var expectedChats = new List<Chat>
        {
            new Chat
            {
                Id = 10,
                Title = "Jag Ekspedisie Gesprek",
                CreatedTimestamp = DateTime.UtcNow,
                Participants = new List<ChatParticipant>
                {
                    new ChatParticipant { UserId = 1 },
                    new ChatParticipant { UserId = 2 }
                }
            },
            new Chat
            {
                Id = 11,
                Title = "Reëlings Klets",
                CreatedTimestamp = DateTime.UtcNow
            }
        };

        _mockDb.Setup(db => db.GetChats(userId)).ReturnsAsync(expectedChats);

        // Act
        var result = await _controller.GetUserChats(userId);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var chatList = Assert.IsAssignableFrom<List<Chat>>(okResult.Value);
        Assert.Equal(2, chatList.Count);
        Assert.Equal(10, chatList[0].Id);
        _mockDb.Verify(db => db.GetChats(userId), Times.Once);
    }

    [Fact]
    public async Task GetUserChats_WhenAccessingAnotherUsersChats_Returns403Forbidden()
    {
        // Arrange: User 1 tries to access User 2's chats
        SetCaller(userId: 1);

        // Act
        var result = await _controller.GetUserChats(userId: 2);

        // Assert
        AssertStatusCode(result, 403);
        _mockDb.Verify(db => db.GetChats(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task GetUserChats_WhenAdminAccessesAnotherUsersChats_ReturnsOk()
    {
        // Arrange: Admin (userId 99) accesses User 2's chats
        SetCaller(userId: 99, isAdmin: true);

        var adminChats = new List<Chat>
        {
            new Chat { Id = 12, Title = "Admin View Chat" }
        };
        _mockDb.Setup(db => db.GetChats(2)).ReturnsAsync(adminChats);

        // Act
        var result = await _controller.GetUserChats(userId: 2);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var chatList = Assert.IsAssignableFrom<List<Chat>>(okResult.Value);
        Assert.Single(chatList);
        _mockDb.Verify(db => db.GetChats(2), Times.Once);
    }

    [Fact]
    public async Task GetUserChats_WhenInvalidUserId_ReturnsBadRequest()
    {
        // Act
        var result = await _controller.GetUserChats(userId: 0);

        // Assert
        AssertStatusCode(result, 400);
        _mockDb.Verify(db => db.GetChats(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task GetUserChats_AnonymousCallerRequestingAnotherUsersId_ReturnsUnauthorized()
    {
        // Defense in depth: even without SetCaller (no authenticated identity),
        // requesting a specific userId must never fall through to "allow" just
        // because TryGetCurrentUserId returned false.
        var result = await _controller.GetUserChats(userId: 2);

        Assert.IsType<UnauthorizedObjectResult>(result);
        _mockDb.Verify(db => db.GetChats(It.IsAny<int>()), Times.Never);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 2. GetChatMessages Tests
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetChatMessages_WithValidChat_ReturnsOkWithMessages()
    {
        // Arrange
        SetCaller(userId: 1);
        int chatId = 5;
        var expectedMessages = new List<Message>
        {
            new Message { Id = 1, ChatId = chatId, SenderId = 1, SenderName = "Jan", Content = "Goeiedag!", SentAt = DateTime.UtcNow.AddMinutes(-10) },
            new Message { Id = 2, ChatId = chatId, SenderId = 2, SenderName = "Piet", Content = "Hallo Jan!", SentAt = DateTime.UtcNow.AddMinutes(-5) }
        };

        _mockDb.Setup(db => db.GetChatMessagesPaginated(chatId, 1, 50)).ReturnsAsync(expectedMessages);

        // Act
        var result = await _controller.GetChatMessages(chatId);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var messages = Assert.IsAssignableFrom<List<Message>>(okResult.Value);
        Assert.Equal(2, messages.Count);
        Assert.Equal("Goeiedag!", messages[0].Content);
        _mockDb.Verify(db => db.GetChatMessagesPaginated(chatId, 1, 50), Times.Once);
    }

    [Fact]
    public async Task GetChatMessages_WithPaginationParameters_PassesPaginationToDb()
    {
        // Arrange
        SetCaller(userId: 1);
        int chatId = 8;
        int page = 2;
        int pageSize = 15;

        var pagedMessages = new List<Message>
        {
            new Message { Id = 3, ChatId = chatId, SenderId = 1, Content = "Boodskap bladsy 2", SentAt = DateTime.UtcNow }
        };

        _mockDb.Setup(db => db.GetChatMessagesPaginated(chatId, page, pageSize)).ReturnsAsync(pagedMessages);

        // Act
        var result = await _controller.GetChatMessages(chatId, page: page, pageSize: pageSize);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var messages = Assert.IsAssignableFrom<List<Message>>(okResult.Value);
        Assert.Single(messages);
        Assert.Equal("Boodskap bladsy 2", messages[0].Content);
        _mockDb.Verify(db => db.GetChatMessagesPaginated(chatId, page, pageSize), Times.Once);
    }

    [Fact]
    public async Task GetChatMessages_WithInvalidChatId_ReturnsBadRequest()
    {
        // Act
        var result = await _controller.GetChatMessages(chatId: -1);

        // Assert
        AssertStatusCode(result, 400);
        _mockDb.Verify(db => db.GetChatMessagesPaginated(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task GetChatMessages_AnonymousCaller_ReturnsUnauthorized()
    {
        // Defense in depth: without an authenticated identity, the participant
        // check must never be skipped - a chat's messages must not leak just
        // because TryGetCurrentUserId returned false.
        var result = await _controller.GetChatMessages(chatId: 5);

        Assert.IsType<UnauthorizedObjectResult>(result);
        _mockDb.Verify(db => db.GetChatMessagesPaginated(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 3. CreateMessage Tests
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateMessage_WithValidPayload_ReturnsOkWithMessageIdAndBroadcasts()
    {
        // Arrange
        int chatId = 10;
        int senderId = 1;
        SetCaller(senderId);

        var request = new SendMessageRequest
        {
            SenderId = senderId,
            Content = "Nuutste boodskap vir klets 10"
        };

        int createdMessageId = 501;
        _mockDb.Setup(db => db.CreateMessage(chatId, senderId, request.Content)).ReturnsAsync(createdMessageId);
        _mockDb.Setup(db => db.GetSenderName(senderId)).ReturnsAsync("Kobus Pretorius");

        // Act
        var result = await _controller.CreateMessage(chatId, request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);

        var idProp = okResult.Value.GetType().GetProperty("messageId");
        Assert.NotNull(idProp);
        Assert.Equal(createdMessageId, idProp.GetValue(okResult.Value));

        _mockDb.Verify(db => db.CreateMessage(chatId, senderId, request.Content), Times.Once);
        // Verify SignalR hub group broadcast was executed
        _mockClients.Verify(c => c.Group(chatId.ToString()), Times.Once);
        _mockClientProxy.Verify(p => p.SendCoreAsync("ReceiveMessage", It.IsAny<object[]>(), default), Times.Once);
    }

    [Fact]
    public async Task CreateMessage_WhenSenderSpoofingOrIDOR_Returns403Forbidden()
    {
        // Arrange: Authenticated as User 1, but payload specifies SenderId 2 (Spoofing/IDOR)
        SetCaller(userId: 1);

        var request = new SendMessageRequest
        {
            SenderId = 2,
            Content = "Probeer 'n vals afsender boodskap stuur"
        };

        // Act
        var result = await _controller.CreateMessage(chatId: 10, request: request);

        // Assert
        AssertStatusCode(result, 403);
        _mockDb.Verify(db => db.CreateMessage(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CreateMessage_WhenPayloadNull_ReturnsBadRequest()
    {
        // Act
        var result = await _controller.CreateMessage(chatId: 10, request: null!);

        // Assert
        AssertStatusCode(result, 400);
    }

    [Fact]
    public async Task CreateMessage_WhenChatIdInvalid_ReturnsBadRequest()
    {
        // Act
        var result = await _controller.CreateMessage(chatId: 0, new SendMessageRequest { SenderId = 1, Content = "Hallo" });

        // Assert
        AssertStatusCode(result, 400);
    }

    [Fact]
    public async Task CreateMessage_WhenSenderIdZeroOrNegative_ReturnsBadRequest()
    {
        // Act
        var result = await _controller.CreateMessage(chatId: 10, new SendMessageRequest { SenderId = -1, Content = "Boodskap" });

        // Assert
        AssertStatusCode(result, 400);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateMessage_WhenContentIsNullOrEmptyOrWhitespace_ReturnsBadRequest(string? emptyContent)
    {
        // Arrange
        SetCaller(userId: 1);
        var request = new SendMessageRequest
        {
            SenderId = 1,
            Content = emptyContent!
        };

        // Act
        var result = await _controller.CreateMessage(chatId: 10, request);

        // Assert
        AssertStatusCode(result, 400);
        _mockDb.Verify(db => db.CreateMessage(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CreateMessage_WhenSenderNotParticipantInChat_Returns403Forbidden()
    {
        // Arrange
        int chatId = 10;
        int senderId = 1;
        SetCaller(senderId);

        var request = new SendMessageRequest
        {
            SenderId = senderId,
            Content = "Ek behoort nie hier nie"
        };

        _mockDb.Setup(db => db.CreateMessage(chatId, senderId, request.Content))
            .ThrowsAsync(new InvalidOperationException("Sender is not a participant in this chat."));

        // Act
        var result = await _controller.CreateMessage(chatId, request);

        // Assert
        AssertStatusCode(result, 403);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 4. CreateChat Tests
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateChat_WithValidPayload_ReturnsOkWithChatId()
    {
        // Arrange
        SetCaller(userId: 1);

        var request = new CreateChatRequest
        {
            Title = "Nuwe Groep Klets",
            SenderId = 1,
            Message = "Welkom by die klets!",
            ChatParticipants = new List<ChatParticipant>
            {
                new ChatParticipant { UserId = 1 },
                new ChatParticipant { UserId = 2 },
                new ChatParticipant { UserId = 3 }
            }
        };

        int expectedChatId = 77;
        _mockDb.Setup(db => db.CreateChat(request)).ReturnsAsync(expectedChatId);
        _mockDb.Setup(db => db.GetSenderName(1)).ReturnsAsync("Hendrik Viljoen");

        // Act
        var result = await _controller.CreateChat(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);

        var chatIdProp = okResult.Value.GetType().GetProperty("chatId");
        Assert.NotNull(chatIdProp);
        Assert.Equal(expectedChatId, chatIdProp.GetValue(okResult.Value));

        _mockDb.Verify(db => db.CreateChat(request), Times.Once);
        // Verify broadcast of initial message
        _mockClients.Verify(c => c.Group(expectedChatId.ToString()), Times.Once);
    }

    [Fact]
    public async Task CreateChat_TestsOneOnOneDirectChat_CreatesSuccessfully()
    {
        // Arrange: 1-on-1 direct conversation between User 1 and User 4
        SetCaller(userId: 1);

        var request = new CreateChatRequest
        {
            Title = "Direkte Klets: Jan & Piet",
            SenderId = 1,
            Message = "Goeiedag Piet, kom ons praat direk.",
            ChatParticipants = new List<ChatParticipant>
            {
                new ChatParticipant { UserId = 1 },
                new ChatParticipant { UserId = 4 }
            }
        };

        int expectedDirectChatId = 88;
        _mockDb.Setup(db => db.CreateChat(request)).ReturnsAsync(expectedDirectChatId);
        _mockDb.Setup(db => db.GetSenderName(1)).ReturnsAsync("Jan");

        // Act
        var result = await _controller.CreateChat(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);

        var chatIdProp = okResult.Value.GetType().GetProperty("chatId");
        Assert.NotNull(chatIdProp);
        Assert.Equal(expectedDirectChatId, chatIdProp.GetValue(okResult.Value));

        _mockDb.Verify(db => db.CreateChat(request), Times.Once);
    }

    [Fact]
    public async Task CreateChat_WhenRequestNull_ReturnsBadRequest()
    {
        // Act
        var result = await _controller.CreateChat(null!);

        // Assert
        AssertStatusCode(result, 400);
        _mockDb.Verify(db => db.CreateChat(It.IsAny<CreateChatRequest>()), Times.Never);
    }

    [Fact]
    public async Task CreateChat_WhenParticipantsEmpty_ReturnsBadRequest()
    {
        // Arrange
        var request = new CreateChatRequest
        {
            SenderId = 1,
            Message = "Geen lede nie",
            ChatParticipants = new List<ChatParticipant>()
        };

        // Act
        var result = await _controller.CreateChat(request);

        // Assert
        AssertStatusCode(result, 400);
        _mockDb.Verify(db => db.CreateChat(It.IsAny<CreateChatRequest>()), Times.Never);
    }

    [Fact]
    public async Task CreateChat_WhenInitialMessageIsEmpty_ReturnsBadRequest()
    {
        // Arrange
        SetCaller(userId: 1);
        var request = new CreateChatRequest
        {
            SenderId = 1,
            Message = "   ",
            ChatParticipants = new List<ChatParticipant> { new ChatParticipant { UserId = 1 } }
        };

        // Act
        var result = await _controller.CreateChat(request);

        // Assert
        AssertStatusCode(result, 400);
        _mockDb.Verify(db => db.CreateChat(It.IsAny<CreateChatRequest>()), Times.Never);
    }

    [Fact]
    public async Task CreateChat_WhenSenderSpoofing_Returns403Forbidden()
    {
        // Arrange: Logged in as User 1, but sets SenderId = 2
        SetCaller(userId: 1);
        var request = new CreateChatRequest
        {
            SenderId = 2,
            Message = "Spoofing sender",
            ChatParticipants = new List<ChatParticipant> { new ChatParticipant { UserId = 2 } }
        };

        // Act
        var result = await _controller.CreateChat(request);

        // Assert
        AssertStatusCode(result, 403);
        _mockDb.Verify(db => db.CreateChat(It.IsAny<CreateChatRequest>()), Times.Never);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 5. SetReadStatus Tests
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SetReadStatus_WhenSuccess_UpdatesReadPointerAndReturns200Ok()
    {
        // Arrange
        int chatId = 10;
        int userId = 1;
        int lastReadMessageId = 120;
        SetCaller(userId);

        var request = new SetReadStatusRequest
        {
            ChatId = chatId,
            UserId = userId,
            LastReadMessageId = lastReadMessageId
        };

        _mockDb.Setup(db => db.IsParticipant(chatId, userId)).ReturnsAsync(true);
        _mockDb.Setup(db => db.SetReadStatus(chatId, userId, lastReadMessageId)).ReturnsAsync(true);

        // Act
        var result = await _controller.SetReadStatus(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);

        _mockDb.Verify(db => db.SetReadStatus(chatId, userId, lastReadMessageId), Times.Once);
        // Verify broadcast notification for read receipt
        _mockClients.Verify(c => c.Group(chatId.ToString()), Times.Once);
        _mockClientProxy.Verify(p => p.SendCoreAsync("MessagesRead", It.IsAny<object[]>(), default), Times.Once);
    }

    [Fact]
    public async Task SetReadStatus_WhenUserNotParticipant_Returns403Forbidden()
    {
        // Arrange: User 1 is not a participant in chat 10
        int chatId = 10;
        int userId = 1;
        SetCaller(userId);

        var request = new SetReadStatusRequest
        {
            ChatId = chatId,
            UserId = userId,
            LastReadMessageId = 50
        };

        _mockDb.Setup(db => db.IsParticipant(chatId, userId)).ReturnsAsync(false);

        // Act
        var result = await _controller.SetReadStatus(request);

        // Assert
        AssertStatusCode(result, 403);
        _mockDb.Verify(db => db.SetReadStatus(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task SetReadStatus_WhenDbUpdateReturnsFalse_Returns403Forbidden()
    {
        // Arrange: DB cannot update read status (e.g., user removed from chat)
        int chatId = 10;
        int userId = 1;
        SetCaller(userId);

        var request = new SetReadStatusRequest
        {
            ChatId = chatId,
            UserId = userId,
            LastReadMessageId = 50
        };

        _mockDb.Setup(db => db.IsParticipant(chatId, userId)).ReturnsAsync(true);
        _mockDb.Setup(db => db.SetReadStatus(chatId, userId, 50)).ReturnsAsync(false);

        // Act
        var result = await _controller.SetReadStatus(request);

        // Assert
        AssertStatusCode(result, 403);
        _mockDb.Verify(db => db.SetReadStatus(chatId, userId, 50), Times.Once);
    }

    [Fact]
    public async Task SetReadStatus_WhenCallerAttemptsToUpdateOtherUsersReadStatus_Returns403Forbidden()
    {
        // Arrange: Logged in as User 1, but requests read update for User 2
        SetCaller(userId: 1);

        var request = new SetReadStatusRequest
        {
            ChatId = 10,
            UserId = 2,
            LastReadMessageId = 50
        };

        // Act
        var result = await _controller.SetReadStatus(request);

        // Assert
        AssertStatusCode(result, 403);
        _mockDb.Verify(db => db.SetReadStatus(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task SetReadStatus_WhenInvalidRequest_ReturnsBadRequest()
    {
        // Act
        var result = await _controller.SetReadStatus(new SetReadStatusRequest { ChatId = 0, UserId = 1 });

        // Assert
        AssertStatusCode(result, 400);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 6. GetNotificationStatus Tests
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetNotificationStatus_WhenAuthorized_ReturnsAccurateUnreadCount()
    {
        // Arrange
        int userId = 1;
        SetCaller(userId);

        var expectedStatus = new NotificationStatus
        {
            IsEnabled = true,
            UnreadCount = 9,
            LastNotified = DateTime.UtcNow
        };

        _mockDb.Setup(db => db.GetNotificationStatus(userId)).ReturnsAsync(expectedStatus);

        // Act
        var result = await _controller.GetNotificationStatus(userId);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var status = Assert.IsType<NotificationStatus>(okResult.Value);
        Assert.True(status.IsEnabled);
        Assert.Equal(9, status.UnreadCount);
        _mockDb.Verify(db => db.GetNotificationStatus(userId), Times.Once);
    }

    [Fact]
    public async Task GetNotificationStatus_WhenCallerAccessesOtherUsersNotifications_Returns403Forbidden()
    {
        // Arrange: User 1 tries to access User 2's notification status
        SetCaller(userId: 1);

        // Act
        var result = await _controller.GetNotificationStatus(userId: 2);

        // Assert
        AssertStatusCode(result, 403);
        _mockDb.Verify(db => db.GetNotificationStatus(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task GetNotificationStatus_WhenInvalidUserId_ReturnsBadRequest()
    {
        // Act
        var result = await _controller.GetNotificationStatus(userId: -5);

        // Assert
        AssertStatusCode(result, 400);
        _mockDb.Verify(db => db.GetNotificationStatus(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task GetNotificationStatus_AnonymousCallerRequestingAnotherUsersId_ReturnsUnauthorized()
    {
        // Defense in depth: no authenticated identity must never fall through to
        // "allow" for an explicitly-requested userId.
        var result = await _controller.GetNotificationStatus(userId: 2);

        Assert.IsType<UnauthorizedObjectResult>(result);
        _mockDb.Verify(db => db.GetNotificationStatus(It.IsAny<int>()), Times.Never);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 7. DeleteMessage Tests
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteMessage_SenderCanDeleteOwnMessage_Returns200Ok()
    {
        // Arrange
        int messageId = 33;
        int userId = 1;
        SetCaller(userId);

        _mockDb.Setup(db => db.DeleteMessage(messageId, userId)).ReturnsAsync(true);

        // Act
        var result = await _controller.DeleteMessage(messageId, userId);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
        _mockDb.Verify(db => db.DeleteMessage(messageId, userId), Times.Once);
    }

    [Fact]
    public async Task DeleteMessage_WhenCallerAttemptsToDeleteAsAnotherUser_Returns403Forbidden()
    {
        // Arrange: Logged in as User 1, but invokes delete with userId = 2
        SetCaller(userId: 1);

        // Act
        var result = await _controller.DeleteMessage(id: 33, userId: 2);

        // Assert
        AssertStatusCode(result, 403);
        _mockDb.Verify(db => db.DeleteMessage(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task DeleteMessage_WhenCallerIsNotMessageSenderInDb_Returns403Forbidden()
    {
        // Arrange: DB delete returns false because user does not own message
        int messageId = 33;
        int userId = 1;
        SetCaller(userId);

        _mockDb.Setup(db => db.DeleteMessage(messageId, userId)).ReturnsAsync(false);

        // Act
        var result = await _controller.DeleteMessage(messageId, userId);

        // Assert
        AssertStatusCode(result, 403);
        _mockDb.Verify(db => db.DeleteMessage(messageId, userId), Times.Once);
    }

    [Fact]
    public async Task DeleteMessage_WhenInvalidParameters_ReturnsBadRequest()
    {
        // Act
        var result = await _controller.DeleteMessage(id: 0, userId: 1);

        // Assert
        AssertStatusCode(result, 400);
        _mockDb.Verify(db => db.DeleteMessage(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 8. Fase 1 & 2 Canonical Routes & Idempotency Tests
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CreateChatRequest_SupportsBothInitialMessageAndMessageProperties()
    {
        var req1 = new CreateChatRequest { InitialMessage = "Initial Hello" };
        Assert.Equal("Initial Hello", req1.Message);
        Assert.Equal("Initial Hello", req1.InitialMessage);

        var req2 = new CreateChatRequest { Message = "Direct Hello" };
        Assert.Equal("Direct Hello", req2.Message);
        Assert.Equal("Direct Hello", req2.InitialMessage);
    }

    [Fact]
    public async Task GetOrCreateDirectChat_WithValidUsers_ReturnsOk()
    {
        // Arrange
        SetCaller(1);
        int targetUserId = 2;
        var request = new DirectChatRequest { InitialMessage = "Hallo Jan!" };

        _mockDb.Setup(db => db.GetOrCreateDirectChat(1, targetUserId, "Hallo Jan!")).ReturnsAsync(105);
        _mockDb.Setup(db => db.GetSenderName(1)).ReturnsAsync("Helgard");

        // Act
        var result = await _controller.GetOrCreateDirectChat(targetUserId, request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
        _mockClients.Verify(c => c.Group("105"), Times.Once);
        _mockClientProxy.Verify(p => p.SendCoreAsync("ReceiveMessage", It.IsAny<object[]>(), default), Times.Once);
    }

    [Fact]
    public async Task GetOrCreateDirectChat_WhenSelf_ReturnsBadRequest()
    {
        // Arrange: User 1 trying to direct chat with User 1
        SetCaller(1);

        // Act
        var result = await _controller.GetOrCreateDirectChat(1, new DirectChatRequest());

        // Assert
        AssertStatusCode(result, 400);
    }

    [Fact]
    public async Task SetReadStatusRoute_WhenValid_ReturnsOkAndBroadcasts()
    {
        // Arrange
        int chatId = 15;
        int userId = 1;
        int lastReadId = 88;
        SetCaller(userId);

        var request = new SetReadStatusRequest
        {
            UserId = userId,
            LastReadMessageId = lastReadId
        };

        _mockDb.Setup(db => db.IsParticipant(chatId, userId)).ReturnsAsync(true);
        _mockDb.Setup(db => db.SetReadStatus(chatId, userId, lastReadId)).ReturnsAsync(true);

        // Act
        var result = await _controller.SetReadStatusForChat(chatId, request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
        _mockClients.Verify(c => c.Group(chatId.ToString()), Times.Once);
        _mockClientProxy.Verify(p => p.SendCoreAsync("MessagesRead", It.IsAny<object[]>(), default), Times.Once);
    }

    [Fact]
    public async Task GetCurrentUserChats_WhenAuthenticated_ReturnsOk()
    {
        // Arrange
        int currentUserId = 1;
        SetCaller(currentUserId);

        var expectedChats = new List<Chat>
        {
            new Chat { Id = 1, Title = "Klets 1" }
        };
        _mockDb.Setup(db => db.GetChats(currentUserId)).ReturnsAsync(expectedChats);

        // Act
        var result = await _controller.GetUserChats();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var chats = Assert.IsAssignableFrom<List<Chat>>(okResult.Value);
        Assert.Single(chats);
    }

    [Fact]
    public async Task GetCurrentUserNotificationStatus_WhenAuthenticated_ReturnsOk()
    {
        // Arrange
        int currentUserId = 1;
        SetCaller(currentUserId);

        var status = new NotificationStatus { IsEnabled = true, UnreadCount = 3 };
        _mockDb.Setup(db => db.GetNotificationStatus(currentUserId)).ReturnsAsync(status);

        // Act
        var result = await _controller.GetNotificationStatus();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var retStatus = Assert.IsType<NotificationStatus>(okResult.Value);
        Assert.Equal(3, retStatus.UnreadCount);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 9. Multi-Role Scenarios & Idempotency Verification
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task MultiRole_HunterJohanToPhHendrik_DirectChat_InitiatesSuccessfullyAndBroadcasts()
    {
        // Scenario: Hunter Johan (User 1) initiates direct chat with PH Hendrik (User 4)
        int hunterId = 1;
        int phId = 4;
        SetCaller(hunterId);

        var request = new DirectChatRequest
        {
            SenderId = hunterId,
            InitialMessage = "More Hendrik, ek stel belang in 'n jagpakket vir Mei."
        };

        int expectedChatId = 201;
        _mockDb.Setup(db => db.GetOrCreateDirectChat(hunterId, phId, request.InitialMessage))
            .ReturnsAsync(expectedChatId);
        _mockDb.Setup(db => db.GetSenderName(hunterId))
            .ReturnsAsync("Johan van der Merwe");

        // Act
        var result = await _controller.GetOrCreateDirectChat(phId, request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);

        var chatIdProp = okResult.Value.GetType().GetProperty("chatId");
        Assert.NotNull(chatIdProp);
        Assert.Equal(expectedChatId, chatIdProp.GetValue(okResult.Value));

        _mockDb.Verify(db => db.GetOrCreateDirectChat(hunterId, phId, request.InitialMessage), Times.Once);
        _mockClients.Verify(c => c.Group(expectedChatId.ToString()), Times.Once);
        _mockClientProxy.Verify(p => p.SendCoreAsync("ReceiveMessage", It.IsAny<object[]>(), default), Times.Once);
    }

    [Fact]
    public async Task MultiRole_HunterJohanToPjUser_DirectChat_InitiatesSuccessfullyAndBroadcasts()
    {
        // Scenario: Hunter Johan (User 1) initiates direct chat with PJ User (User 19)
        int hunterId = 1;
        int pjId = 19;
        SetCaller(hunterId);

        var request = new DirectChatRequest
        {
            SenderId = hunterId,
            InitialMessage = "Goeiedag, is u beskikbaar vir foto- en video-opnames tydens my jag?"
        };

        int expectedChatId = 202;
        _mockDb.Setup(db => db.GetOrCreateDirectChat(hunterId, pjId, request.InitialMessage))
            .ReturnsAsync(expectedChatId);
        _mockDb.Setup(db => db.GetSenderName(hunterId))
            .ReturnsAsync("Johan van der Merwe");

        // Act
        var result = await _controller.GetOrCreateDirectChat(pjId, request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);

        var chatIdProp = okResult.Value.GetType().GetProperty("chatId");
        Assert.NotNull(chatIdProp);
        Assert.Equal(expectedChatId, chatIdProp.GetValue(okResult.Value));

        _mockDb.Verify(db => db.GetOrCreateDirectChat(hunterId, pjId, request.InitialMessage), Times.Once);
        _mockClients.Verify(c => c.Group(expectedChatId.ToString()), Times.Once);
    }

    [Fact]
    public async Task DirectChat_CreationIdempotency_MultipleClicks_DoNotCreateDuplicateChats()
    {
        // Scenario: User 1 clicks message button 3 times in quick succession for User 4
        int hunterId = 1;
        int phId = 4;
        SetCaller(hunterId);

        const int canonicalChatId = 205;

        // DB returns the same canonicalChatId for every call
        _mockDb.Setup(db => db.GetOrCreateDirectChat(hunterId, phId, It.IsAny<string?>()))
            .ReturnsAsync(canonicalChatId);
        _mockDb.Setup(db => db.GetSenderName(hunterId))
            .ReturnsAsync("Johan van der Merwe");

        // Act - Simulate 3 consecutive clicks/requests
        var result1 = await _controller.GetOrCreateDirectChat(phId, new DirectChatRequest { InitialMessage = "Kliek 1" });
        var result2 = await _controller.GetOrCreateDirectChat(phId, new DirectChatRequest { InitialMessage = null });
        var result3 = await _controller.GetOrCreateDirectChat(phId, new DirectChatRequest { InitialMessage = null });

        // Assert
        var ok1 = Assert.IsType<OkObjectResult>(result1);
        var ok2 = Assert.IsType<OkObjectResult>(result2);
        var ok3 = Assert.IsType<OkObjectResult>(result3);

        var id1 = ok1.Value?.GetType().GetProperty("chatId")?.GetValue(ok1.Value);
        var id2 = ok2.Value?.GetType().GetProperty("chatId")?.GetValue(ok2.Value);
        var id3 = ok3.Value?.GetType().GetProperty("chatId")?.GetValue(ok3.Value);

        Assert.Equal(canonicalChatId, id1);
        Assert.Equal(canonicalChatId, id2);
        Assert.Equal(canonicalChatId, id3);

        _mockDb.Verify(db => db.GetOrCreateDirectChat(hunterId, phId, It.IsAny<string?>()), Times.Exactly(3));
    }

    [Fact]
    public async Task MultiRole_HunterJohan_CannotView_PhHendrik_PrivateChats_IDOR_Returns403()
    {
        // Scenario: Hunter Johan (User 1) attempts to inspect PH Hendrik's (User 4) private chats
        SetCaller(userId: 1);

        // Act
        var result = await _controller.GetUserChats(userId: 4);

        // Assert
        AssertStatusCode(result, 403);
        _mockDb.Verify(db => db.GetChats(4), Times.Never);
    }

    [Fact]
    public async Task MultiRole_HunterJohan_CannotMarkReadOrDelete_PhHendrikMessages_Returns403()
    {
        // Scenario: Hunter Johan (User 1) attempts to mark read on behalf of PH Hendrik (User 4)
        SetCaller(userId: 1);

        var readReq = new SetReadStatusRequest
        {
            ChatId = 201,
            UserId = 4, // Attacking by setting another user's ID
            LastReadMessageId = 55
        };

        var readResult = await _controller.SetReadStatus(readReq);
        AssertStatusCode(readResult, 403);

        // Hunter Johan attempts to delete PH Hendrik's message
        var delResult = await _controller.DeleteMessage(id: 99, userId: 4);
        AssertStatusCode(delResult, 403);
    }
}
