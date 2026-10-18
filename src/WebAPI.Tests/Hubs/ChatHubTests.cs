using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Moq;
using WebAPI.Database;
using WebAPI.Hubs;
using WebAPI.Models;
using Xunit;

namespace WebAPI.Tests.Hubs;

public class ChatHubTests
{
    private readonly Mock<HubCallerContext> _mockContext;
    private readonly Mock<IGroupManager> _mockGroups;
    private readonly Mock<IHubCallerClients> _mockClients;
    private readonly Mock<IClientProxy> _mockClientProxy;
    private readonly Mock<Chats_db> _mockDb;
    private readonly ChatHub _hub;

    private const string ConnectionId = "conn_test_123";
    private const int CallerUserId = 1;

    public ChatHubTests()
    {
        _mockContext = new Mock<HubCallerContext>();
        _mockGroups = new Mock<IGroupManager>();
        _mockClients = new Mock<IHubCallerClients>();
        _mockClientProxy = new Mock<IClientProxy>();

        _mockContext.Setup(c => c.ConnectionId).Returns(ConnectionId);
        _mockContext.Setup(c => c.User).Returns(new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, CallerUserId.ToString()) }, "Test")));
        _mockContext.Setup(c => c.Items).Returns(new Dictionary<object, object?>());

        _mockDb = new Mock<Chats_db>("DummyConnectionString");
        _mockDb.Setup(d => d.IsParticipant(42, CallerUserId)).ReturnsAsync(true);
        _mockDb.Setup(d => d.IsParticipant(99, CallerUserId)).ReturnsAsync(false);

        _mockClients.Setup(c => c.Group(It.IsAny<string>())).Returns(_mockClientProxy.Object);
        _mockClients.Setup(c => c.Groups(It.IsAny<IReadOnlyList<string>>())).Returns(_mockClientProxy.Object);

        _hub = new ChatHub(_mockDb.Object)
        {
            Context = _mockContext.Object,
            Groups = _mockGroups.Object,
            Clients = _mockClients.Object
        };
    }

    [Fact]
    public async Task JoinChat_AddsConnectionToBothChatGroupFormats()
    {
        // Act
        await _hub.JoinChat(42);

        // Assert
        _mockGroups.Verify(g => g.AddToGroupAsync(ConnectionId, "42", default), Times.Once);
        _mockGroups.Verify(g => g.AddToGroupAsync(ConnectionId, "chat_42", default), Times.Once);
    }

    [Fact]
    public async Task LeaveChat_RemovesConnectionFromBothChatGroupFormats()
    {
        // Act
        await _hub.LeaveChat(42);

        // Assert
        _mockGroups.Verify(g => g.RemoveFromGroupAsync(ConnectionId, "42", default), Times.Once);
        _mockGroups.Verify(g => g.RemoveFromGroupAsync(ConnectionId, "chat_42", default), Times.Once);
    }

    [Fact]
    public async Task SendMessage_BroadcastsToChatGroups()
    {
        // Arrange
        var message = new Message
        {
            Id = 100,
            ChatId = 42,
            SenderId = 1,
            SenderName = "Johan",
            Content = "Hallo Hendrik!",
            SentAt = DateTime.UtcNow
        };

        // Act
        await _hub.JoinChat(42);
        await _hub.SendMessage(42, message);

        // Assert
        _mockClients.Verify(c => c.Groups(It.Is<IReadOnlyList<string>>(l => l.Contains("42") && l.Contains("chat_42"))), Times.Once);
        _mockClientProxy.Verify(p => p.SendCoreAsync("ReceiveMessage", It.Is<object[]>(o => o.Length == 1 && o[0] == message), default), Times.Once);
    }

    [Fact]
    public async Task SendTyping_BroadcastsToChatGroups()
    {
        // Act
        await _hub.JoinChat(42);
        await _hub.SendTyping(42, 1, "Johan");

        // Assert
        _mockClientProxy.Verify(p => p.SendCoreAsync("UserTyping", It.IsAny<object[]>(), default), Times.Exactly(2));
    }

    [Fact]
    public async Task SendStoppedTyping_BroadcastsToChatGroups()
    {
        // Act
        await _hub.JoinChat(42);
        await _hub.SendStoppedTyping(42, 1);

        // Assert
        _mockClientProxy.Verify(p => p.SendCoreAsync("UserStoppedTyping", It.IsAny<object[]>(), default), Times.Exactly(2));
    }

    [Fact]
    public async Task MarkMessagesAsRead_BroadcastsToChatGroups()
    {
        // Act
        await _hub.JoinChat(42);
        await _hub.MarkMessagesAsRead(42, 1, 100);

        // Assert
        _mockClientProxy.Verify(p => p.SendCoreAsync("MessagesRead", It.IsAny<object[]>(), default), Times.Exactly(2));
    }

    [Fact]
    public async Task JoinChat_NonParticipant_ThrowsAndDoesNotJoinGroups()
    {
        await Assert.ThrowsAsync<HubException>(() => _hub.JoinChat(99));

        _mockGroups.Verify(g => g.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), default), Times.Never);
    }

    [Fact]
    public async Task SendTyping_WithoutJoiningChat_Throws()
    {
        await Assert.ThrowsAsync<HubException>(() => _hub.SendTyping(42, CallerUserId, "Johan"));

        _mockClientProxy.Verify(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), default), Times.Never);
    }

    [Fact]
    public async Task SendTyping_AsAnotherUser_Throws()
    {
        await _hub.JoinChat(42);

        await Assert.ThrowsAsync<HubException>(() => _hub.SendTyping(42, 7, "Iemand anders"));
    }

    [Fact]
    public async Task LeaveChat_StopsFurtherBroadcasts()
    {
        await _hub.JoinChat(42);
        await _hub.LeaveChat(42);

        await Assert.ThrowsAsync<HubException>(() => _hub.SendStoppedTyping(42, CallerUserId));
    }

    [Fact]
    public async Task Lifecycle_OnConnectedAndDisconnected_CompleteSuccessfully()
    {
        // Act & Assert (Should not throw)
        await _hub.OnConnectedAsync();
        await _hub.OnDisconnectedAsync(null);
    }
}
