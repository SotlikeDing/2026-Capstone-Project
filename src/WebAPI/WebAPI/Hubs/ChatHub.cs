using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using WebAPI.Controllers;
using WebAPI.Database;
using WebAPI.Models;

namespace WebAPI.Hubs;

/// <summary>
/// SignalR hub for real-time chat: group membership per chat (two group-key formats
/// are maintained in parallel, see below) plus fan-out of message/typing/read-receipt
/// events. Message persistence itself happens separately via ChatsController/Chats_db -
/// this hub only pushes live notifications to already-connected clients.
///
/// Requires a JWT (the WebApp passes it via AccessTokenProvider). A connection may only
/// join a chat its user participates in, and may only broadcast to chats it has joined,
/// under its own userId - so no eavesdropping on or spoofing into other people's chats.
/// </summary>
[Authorize]
public class ChatHub : Hub
{
    private const string JoinedChatsKey = "joinedChats";
    private readonly Chats_db _db;

    public ChatHub(Chats_db db)
    {
        _db = db;
    }

    /// <summary>The set of chatIds this connection has successfully joined (kept in the per-connection Context.Items).</summary>
    private HashSet<int> JoinedChats
    {
        get
        {
            if (Context.Items.TryGetValue(JoinedChatsKey, out var existing) && existing is HashSet<int> set) return set;
            var created = new HashSet<int>();
            Context.Items[JoinedChatsKey] = created;
            return created;
        }
    }

    /// <summary>The caller's userId from the JWT; throws when missing (should not happen behind [Authorize]).</summary>
    private int CallerUserId => Context.User?.GetUserId() ?? throw new HubException("Nie aangemeld nie.");

    /// <summary>Rejects a broadcast unless this connection joined chatId and userId (when given) is the caller.</summary>
    private void EnsureCanBroadcast(int chatId, int? userId = null)
    {
        if (!JoinedChats.Contains(chatId))
            throw new HubException("Geen toegang tot hierdie klets nie.");
        if (userId.HasValue && userId.Value != CallerUserId)
            throw new HubException("Gebruiker stem nie ooreen met die aangemelde gebruiker nie.");
    }

    /// <summary>No-op override, present so a breakpoint/log point can be added here later if needed.</summary>
    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();
    }

    /// <summary>No-op override, present so a breakpoint/log point can be added here later if needed.</summary>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Subscribes the caller's connection to chatId's groups, after verifying the caller
    /// is a current participant of that chat (Chats_db.IsParticipant).
    /// </summary>
    public async Task JoinChat(int chatId)
    {
        if (!await _db.IsParticipant(chatId, CallerUserId))
            throw new HubException("Geen toegang tot hierdie klets nie.");

        await Groups.AddToGroupAsync(Context.ConnectionId, chatId.ToString());
        await Groups.AddToGroupAsync(Context.ConnectionId, $"chat_{chatId}");
        JoinedChats.Add(chatId);
    }

    /// <summary>Removes the caller's connection from both group-key variants for chatId (mirror of JoinChat).</summary>
    public async Task LeaveChat(int chatId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, chatId.ToString());
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"chat_{chatId}");
        JoinedChats.Remove(chatId);
    }

    /// <summary>Broadcasts message to every connection in the chat's group(s) as a "ReceiveMessage" event. Does not persist it - callers separately POST to ChatsController to save the message.</summary>
    public async Task SendMessage(int chatId, Message message)
    {
        EnsureCanBroadcast(chatId, message.SenderId);
        await Clients.Groups(new[] { chatId.ToString(), $"chat_{chatId}" })
            .SendAsync("ReceiveMessage", message);
    }

    /// <summary>
    /// Broadcasts a "UserTyping" indicator to the chat group. Sent to both group-key
    /// formats since JoinChat subscribes a connection to both and different callers may
    /// send with either key.
    /// </summary>
    public async Task SendTyping(int chatId, int userId, string? userName = null)
    {
        EnsureCanBroadcast(chatId, userId);
        var payload = new { chatId, userId, userName };
        await Clients.Group($"chat_{chatId}").SendAsync("UserTyping", payload);
        await Clients.Group(chatId.ToString()).SendAsync("UserTyping", payload);
    }

    /// <summary>Broadcasts a "UserStoppedTyping" event, clearing the typing indicator set by SendTyping.</summary>
    public async Task SendStoppedTyping(int chatId, int userId)
    {
        EnsureCanBroadcast(chatId, userId);
        var payload = new { chatId, userId };
        await Clients.Group($"chat_{chatId}").SendAsync("UserStoppedTyping", payload);
        await Clients.Group(chatId.ToString()).SendAsync("UserStoppedTyping", payload);
    }

    /// <summary>Broadcasts a "MessagesRead" read-receipt event (with a server-stamped readAt) so other participants' UIs can update read state live.</summary>
    public async Task MarkMessagesAsRead(int chatId, int userId, int lastReadMessageId)
    {
        EnsureCanBroadcast(chatId, userId);
        var payload = new { chatId, userId, lastReadMessageId, readAt = DateTime.UtcNow };
        await Clients.Group($"chat_{chatId}").SendAsync("MessagesRead", payload);
        await Clients.Group(chatId.ToString()).SendAsync("MessagesRead", payload);
    }

}
