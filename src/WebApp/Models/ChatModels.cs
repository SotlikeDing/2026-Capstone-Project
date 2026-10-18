using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace WebApp.Models
{
    /// <summary>Client-side DTOs deserialized from WebAPI's ChatsController responses and used by SignalR ChatHub payloads. Property names/JSON keys mirror the WebAPI Models/Chat.cs response shapes.</summary>
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

    /// <summary>One member of a Chat. LeftAtTimestamp non-null means the participant has left/been removed; LastReadMessageId backs the read-receipt indicator.</summary>
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

    /// <summary>A single persisted chat message, as returned by the API and pushed live over SignalR's "ReceiveMessage" event.</summary>
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

    /// <summary>POST body for sending a new chat message.</summary>
    public class SendChatMessageRequest
    {
        [JsonPropertyName("senderId")]
        public int SenderId { get; set; }

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;
    }

    /// <summary>Response from sending a message - carries the persisted message back so the caller can render it immediately without waiting for the SignalR echo.</summary>
    public class SendChatMessageResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("messageId")]
        public int MessageId { get; set; }

        [JsonPropertyName("chatId")]
        public int ChatId { get; set; }

        [JsonPropertyName("message")]
        public Message? Message { get; set; }
    }

    /// <summary>Response from the "find or create a direct chat with this user" endpoint - returns the (possibly newly created) chat's id.</summary>
    public class DirectChatApiResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("chatId")]
        public int ChatId { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>Shape of the SignalR "UserTyping"/"UserStoppedTyping" event payloads sent by ChatHub.</summary>
    public class ChatTypingPayload
    {
        [JsonPropertyName("chatId")]
        public int ChatId { get; set; }

        [JsonPropertyName("userId")]
        public int UserId { get; set; }

        [JsonPropertyName("userName")]
        public string? UserName { get; set; }
    }

    /// <summary>Shape of the SignalR "MessagesRead" read-receipt event payload sent by ChatHub.MarkMessagesAsRead.</summary>
    public class ChatReadPayload
    {
        [JsonPropertyName("chatId")]
        public int ChatId { get; set; }

        [JsonPropertyName("userId")]
        public int UserId { get; set; }

        [JsonPropertyName("lastReadMessageId")]
        public int LastReadMessageId { get; set; }

        [JsonPropertyName("readAt")]
        public DateTime? ReadAt { get; set; }
    }
}
