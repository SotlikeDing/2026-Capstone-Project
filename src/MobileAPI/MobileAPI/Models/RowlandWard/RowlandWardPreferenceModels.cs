using System.Text.Json.Serialization;

namespace MobileApi.Models.RowlandWard;

public sealed class RowlandWardPreferenceResponse
{
    [JsonPropertyName("rowlandWardEnabled")]
    public bool RowlandWardEnabled { get; init; }
}

public sealed class UpdateRowlandWardPreferenceRequest
{
    [JsonPropertyName("rowlandWardEnabled")]
    public bool? RowlandWardEnabled { get; init; }
}
