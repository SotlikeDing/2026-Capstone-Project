using System.Text.Json.Serialization;

namespace WebAPI.Models;

/// <summary>A chat with its participants, last-message preview, and unread count - the shape Chats_db.GetChats returns.</summary>
public class Chat
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("createdTimestamp")]
    public DateTime CreatedTimestamp { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("lastMessage")]
    public string? LastMessage { get; set; }

    [JsonPropertyName("lastMessageTimestamp")]
    public DateTime? LastMessageTimestamp { get; set; }

    [JsonPropertyName("unreadCount")]
    public int UnreadCount { get; set; }

    [JsonPropertyName("participants")]
    public List<ChatParticipant> Participants { get; set; } = new();

    [JsonPropertyName("messages")]
    public List<Message> Messages { get; set; } = new();
}

/// <summary>Unused - no controller or database class references this class.</summary>
public class ChatSummary
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("createdTimestamp")]
    public DateTime? CreatedTimestamp { get; set; }

    [JsonPropertyName("participantCount")]
    public int ParticipantCount => Participants.Count;

    [JsonPropertyName("lastMessage")]
    public string? LastMessage { get; set; }

    [JsonPropertyName("lastMessageTimestamp")]
    public DateTime? LastMessageTimestamp { get; set; }

    [JsonPropertyName("unreadCount")]
    public int UnreadCount { get; set; }

    [JsonPropertyName("participants")]
    public List<ChatParticipant> Participants { get; set; } = new();
}

/// <summary>One participant in a chat.</summary>
public class ChatParticipant
{
    [JsonPropertyName("id")]
    public int? Id { get; set; }

    [JsonPropertyName("chatId")]
    public int? ChatId { get; set; }

    [JsonPropertyName("userId")]
    public int UserId { get; set; }

    [JsonPropertyName("userName")]
    public string? UserName { get; set; }

    [JsonPropertyName("joinedTimestamp")]
    public DateTime JoinedTimestamp { get; set; }

    [JsonPropertyName("leftAtTimestamp")]
    public DateTime? LeftAtTimestamp { get; set; }

    [JsonPropertyName("lastReadMessageId")]
    public int? LastReadMessageId { get; set; }
}

/// <summary>A chat message.</summary>
public class Message
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("chatId")]
    public int ChatId { get; set; }

    [JsonPropertyName("senderId")]
    public int SenderId { get; set; }

    [JsonPropertyName("senderName")]
    public string SenderName { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("sentAt")]
    public DateTime SentAt { get; set; }
}

/// <summary>Unused - no controller or database class references this class.</summary>
public class ChatMessage : Message
{
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp
    {
        get => SentAt;
        set => SentAt = value;
    }
}

/// <summary>
/// Payload for creating a chat. Participants/ChatParticipants and
/// Message/InitialMessage are each two JSON keys for the same backing field/list
/// (client-compatibility aliases) - InitialMessage's setter ignores empty/null so
/// it can never blank out a Message already set via the other key.
/// </summary>
public class CreateChatRequest
{
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("chatParticipants")]
    public List<ChatParticipant> ChatParticipants { get; set; } = new();

    [JsonPropertyName("participants")]
    public List<ChatParticipant>? Participants
    {
        get => ChatParticipants;
        set { if (value != null) ChatParticipants = value; }
    }

    [JsonPropertyName("participantIds")]
    public List<int>? ParticipantIds { get; set; }

    [JsonPropertyName("senderId")]
    public int SenderId { get; set; }

    private string _message = string.Empty;

    [JsonPropertyName("message")]
    public string Message
    {
        get => _message;
        set => _message = value;
    }

    [JsonPropertyName("initialMessage")]
    public string? InitialMessage
    {
        get => _message;
        set
        {
            if (!string.IsNullOrEmpty(value))
                _message = value;
        }
    }
}

/// <summary>Response shape after creating a chat.</summary>
public class CreateChatResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("messages")]
    public string Messages
    {
        get => Message;
        set => Message = value;
    }

    [JsonPropertyName("chatId")]
    public int ChatId { get; set; }
}

/// <summary>Unused - no controller or database class references this class.</summary>
public class ChatPanelInfo
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("chatId")]
    public int ChatId { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("createdTimestamp")]
    public DateTimeOffset CreatedTimestamp { get; set; }

    [JsonPropertyName("participants")]
    public List<ChatParticipant> Participants { get; set; } = new();

    [JsonPropertyName("lastMessage")]
    public string LastMessage { get; set; } = string.Empty;

    [JsonPropertyName("lastMessageTimestamp")]
    public DateTime LastMessageTimestamp { get; set; }

    [JsonPropertyName("unreadCount")]
    public int UnreadCount { get; set; }
}

/// <summary>Unread chat-message count for a user - distinct from the general userNotifications bell (see Chats_db.GetNotificationStatus).</summary>
public class NotificationStatus
{
    [JsonPropertyName("isEnabled")]
    public bool IsEnabled { get; set; }

    [JsonPropertyName("unreadCount")]
    public int UnreadCount { get; set; }

    [JsonPropertyName("lastNotified")]
    public DateTime LastNotified { get; set; }
}

/// <summary>Payload for marking messages read up to a point.</summary>
public class SetReadStatusRequest
{
    [JsonPropertyName("chatId")]
    public int ChatId { get; set; }

    [JsonPropertyName("userId")]
    public int UserId { get; set; }

    [JsonPropertyName("lastReadMessageId")]
    public int LastReadMessageId { get; set; }
}



/// <summary>Payload for finding-or-creating a direct chat - InitialMessage/Message are aliases for the same backing field (see CreateChatRequest's remarks).</summary>
public class DirectChatRequest
{
    [JsonPropertyName("senderId")]
    public int SenderId { get; set; }

    private string? _message;

    [JsonPropertyName("initialMessage")]
    public string? InitialMessage
    {
        get => _message;
        set
        {
            if (!string.IsNullOrEmpty(value))
                _message = value;
        }
    }

    [JsonPropertyName("message")]
    public string? Message
    {
        get => _message;
        set
        {
            if (!string.IsNullOrEmpty(value))
                _message = value;
        }
    }
}
