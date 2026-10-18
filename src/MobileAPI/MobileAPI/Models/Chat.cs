using System.Text.Json.Serialization;

namespace MobileApi.Models;

public class Chat
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("createdTimestamp")]
    public DateTime CreatedTimestamp { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("participants")]
    public List<ChatParticipant> Participants { get; set; } = new();

    [JsonPropertyName("messages")]
    public List<Message> Messages { get; set; } = new();
}

public class ChatParticipant
{
    [JsonPropertyName("id")]
    public int? Id { get; set; }

    [JsonPropertyName("chatId")]
    public int? ChatId { get; set; }

    [JsonPropertyName("userId")]
    public int UserId { get; set; }

    [JsonPropertyName("joinedTimestamp")]
    public DateTime JoinedTimestamp { get; set; }

    [JsonPropertyName("leftAtTimestamp")]
    public DateTime? LeftAtTimestamp { get; set; }

    [JsonPropertyName("lastReadMessageId")]
    public int? LastReadMessageId { get; set; }
}

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

public class ChatMessage
{
    [JsonPropertyName("senderId")]
    public int SenderId { get; set; }

    [JsonPropertyName("senderName")]
    public string SenderName { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    [JsonPropertyName("sentAt")]
    public DateTime SentAt { get; set; }
}

public class CreateChatRequest
{
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("chatParticipants")]
    public List<ChatParticipant> ChatParticipants { get; set; } = new();

    [JsonPropertyName("senderId")]
    public int SenderId { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("initialMessage")]
    public string InitialMessage { get => Message; set => Message = value; }
}

public class CreateChatResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("messages")]
    public string Messages { get; set; } = string.Empty;

    [JsonPropertyName("chatId")]
    public int ChatId { get; set; }
}

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

public class NotificationStatus
{
    [JsonPropertyName("isEnabled")]
    public bool IsEnabled { get; set; }

    [JsonPropertyName("unreadCount")]
    public int UnreadCount { get; set; }

    [JsonPropertyName("lastNotified")]
    public DateTime LastNotified { get; set; }
}

public class SetReadStatusRequest
{
    [JsonPropertyName("chatId")]
    public int ChatId { get; set; }

    [JsonPropertyName("userId")]
    public int UserId { get; set; }

    [JsonPropertyName("lastReadMessageId")]
    public int LastReadMessageId { get; set; }
}

