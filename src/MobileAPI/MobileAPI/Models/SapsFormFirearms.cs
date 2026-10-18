using System.Text.Json.Serialization;

namespace MobileApi.Models;

public class SapsFormFirearms
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;

    [JsonPropertyName("calibre")]
    public string Calibre { get; set; } = string.Empty;

    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("make")]
    public string Make { get; set; } = string.Empty;

    [JsonPropertyName("frameOrReceiverSerialNumber")]
    public string FrameOrReceiverSerialNumber { get; set; } = string.Empty;

    [JsonPropertyName("barrelSerialNumber")]
    public string BarrelSerialNumber { get; set; } = string.Empty;
}

