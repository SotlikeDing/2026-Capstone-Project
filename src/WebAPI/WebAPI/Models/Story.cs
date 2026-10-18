using System.Text.Json.Serialization;

namespace WebAPI.Models;

/// <summary>
/// Unused - appears to be the intended full shape for an expedition "story" (GPS
/// trail via DataPoints), but Story_db never populates or reads DataPoints; only
/// storyDataId/bookingId/expeditionNum are actually persisted anywhere.
/// </summary>
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

/// <summary>Unused - see Story's remarks; no code ever creates or reads a StoryDataPoint.</summary>
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

/// <summary>Payload for creating a storyData row (see Story_db.CreateStory).</summary>
public class CreateStoryRequest
{
    public int SdBookingId { get; set; }
    public long? SdExpeditionNum { get; set; }
}

/// <summary>Payload for updating a storyData row's expedition number.</summary>
public class EditStoryRequest
{
    public long? SdExpeditionNum { get; set; }
}

/// <summary>Response shape for a storyData row.</summary>
public class StoryResponse
{
    public int Id { get; set; }
    public int BookingId { get; set; }
    public long? ExpeditionNum { get; set; }
}
