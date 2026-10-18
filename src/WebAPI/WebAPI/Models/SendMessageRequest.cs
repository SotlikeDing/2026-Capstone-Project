using System.Text.Json.Serialization;

namespace WebAPI.Models;

/// <summary>
/// Payload for sending a chat message. Content and Message are two JSON keys for
/// the very same backing field (client-compatibility alias for older/newer callers
/// that send "content" vs "message") - setting either one sets both, and Message's
/// setter ignores an empty/null value so it can never blank out a Content already
/// set from the other key.
/// </summary>
public class SendMessageRequest
{
    [JsonPropertyName("senderId")]
    public int SenderId { get; set; }

    private string _content = string.Empty;

    [JsonPropertyName("content")]
    public string Content
    {
        get => _content;
        set => _content = value;
    }

    [JsonPropertyName("message")]
    public string? Message
    {
        get => _content;
        set
        {
            if (!string.IsNullOrEmpty(value))
                _content = value;
        }
    }

    [JsonPropertyName("chatId")]
    public int? ChatId { get; set; }
}
