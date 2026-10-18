using System.Text.Json.Serialization;

namespace MobileApi.Models;

public class HuntingPermitResponse
{
    public int Id { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class HuntingPermitApplication
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("userRole")]
    public int UserRole { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("filePath")]
    public string? FilePath { get; set; }
}

public class CreatePermitApplicationRequest
{
    public int FarmId { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string Species { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public SapsForm? SapsForm { get; set; }
}

