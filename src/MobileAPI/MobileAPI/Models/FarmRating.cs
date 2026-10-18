using System.Text.Json.Serialization;

namespace MobileApi.Models;

public class SetFarmRatingRequest
{
    [JsonPropertyName("stars")]
    public int Stars { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

public enum SaveFarmRatingStatus
{
    Success,
    BookingNotFound,
    Forbidden,
    FarmNotFound,
    BookingNotEligible
}

public class SaveFarmRatingResult
{
    public SaveFarmRatingStatus Status { get; set; }
    public int RatingId { get; set; }
    public int FarmId { get; set; }
}

public class FarmRatingSummary
{
    public int FarmId { get; set; }
    public decimal? AverageStars { get; set; }
    public int ReviewCount { get; set; }
}
