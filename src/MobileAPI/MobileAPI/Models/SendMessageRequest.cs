using System.Text.Json.Serialization;

namespace MobileApi.Models;

public class SendMessageRequest
{
    [JsonPropertyName("SenderId")]
    public int SenderId { get; set; }

    [JsonPropertyName("Content")]
    public string Content { get; set; } = string.Empty;
}

