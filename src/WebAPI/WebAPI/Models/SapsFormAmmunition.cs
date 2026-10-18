using System.Text.Json.Serialization;

namespace WebAPI.Models;

/// <summary>One ammunition line item on a SAPS 520 firearm-permit form.</summary>
public class SapsFormAmmunition
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("quantity")]
    public int Quantity { get; set; }
}

