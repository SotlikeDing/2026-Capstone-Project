using System.Text.Json.Serialization;

namespace MobileApi.Models;

public class Animal
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("species")]
    public string Species { get; set; } = string.Empty;

    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("farmId")]
    public int FarmId { get; set; }

    [JsonPropertyName("price")]
    public decimal? Price { get; set; }

    [JsonPropertyName("quota")]
    public long? Quota { get; set; }
}

