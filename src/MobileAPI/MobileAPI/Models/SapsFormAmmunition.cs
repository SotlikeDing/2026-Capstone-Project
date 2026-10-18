using System.Text.Json.Serialization;

namespace MobileApi.Models;

public class SapsFormAmmunition
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("quantity")]
    public int Quantity { get; set; }
}

