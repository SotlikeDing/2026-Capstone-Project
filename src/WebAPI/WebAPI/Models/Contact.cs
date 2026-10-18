using System.Text.Json.Serialization;

namespace WebAPI.Models;

/// <summary>A contactUs table row - the full shape, including fields Landing_db's own ContactResponse doesn't expose.</summary>
public class ContactUs
{
    [JsonPropertyName("contactUsId")]
    public int ContactUsId { get; set; }

    [JsonPropertyName("subject")]
    public string? Subject { get; set; }

    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("senderName")]
    public string? SenderName { get; set; }

    [JsonPropertyName("senderSurname")]
    public string? SenderSurname { get; set; }

    [JsonPropertyName("senderEmail")]
    public string? SenderEmail { get; set; }

    [JsonPropertyName("phoneNumber")]
    public string? PhoneNumber { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("isResolved")]
    public bool IsResolved { get; set; }
}

/// <summary>Response shape for Landing_db.GetContact - the latest contact submission's subject/content/email only.</summary>
public class ContactResponse
{
    public string? Subject { get; set; }
    public string? Content { get; set; }
    public string? Email { get; set; }
}
