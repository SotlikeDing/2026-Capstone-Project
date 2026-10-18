using System.Text.Json.Serialization;

namespace MobileApi.Models;

public class Story
{
    [JsonPropertyName("storyDataId")]
    public int StoryDataId { get; set; }

    [JsonPropertyName("bookingId")]
    public int BookingId { get; set; }

    [JsonPropertyName("expeditionNum")]
    public long? ExpeditionNum { get; set; }

    [JsonPropertyName("dataPoints")]
    public List<StoryDataPoint>? DataPoints { get; set; }
}

public class StoryDataPoint
{
    [JsonPropertyName("dataPointId")]
    public int DataPointId { get; set; }

    [JsonPropertyName("storyDataId")]
    public int StoryDataId { get; set; }

    [JsonPropertyName("gpsPos")]
    public string? GpsPos { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    [JsonPropertyName("isCriticalPoint")]
    public bool IsCriticalPoint { get; set; }

    [JsonPropertyName("mediaFolderPath")]
    public string? MediaFolderPath { get; set; }
}

public class CreateStoryRequest
{
    public int SdBookingId { get; set; }
    public long? SdExpeditionNum { get; set; }
}

public class EditStoryRequest
{
    public long? SdExpeditionNum { get; set; }
}

public class StoryResponse
{
    public int Id { get; set; }
    public int BookingId { get; set; }
    public long? ExpeditionNum { get; set; }
}

