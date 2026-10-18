using System.Text.Json.Serialization;

namespace MobileApi.Models;

public class Rating
{
    [JsonPropertyName("ratingId")]
    public int RatingId { get; set; }

    [JsonPropertyName("bookingId")]
    public int BookingId { get; set; }

    [JsonPropertyName("raterUserId")]
    public int RaterUserId { get; set; }

    [JsonPropertyName("rateeUserId")]
    public int RateeUserId { get; set; }

    [JsonPropertyName("rateeRoleCode")]
    public string RateeRoleCode { get; set; } = string.Empty;

    [JsonPropertyName("stars")]
    public int Stars { get; set; }

    [JsonPropertyName("comment")]
    public string? Comment { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }
}

/// <summary>What the app sends to rate one participant; the rater is always the signed-in user.</summary>
public class TeamRatingRequest
{
    public int RateeUserId { get; set; }
    public string RateeRoleCode { get; set; } = string.Empty;
    public int Stars { get; set; }
    public string? Comment { get; set; }
}

/// <summary>
/// Matches WebAPI's POST api/rating/setRoleRating contract (the app's ratings are
/// relayed there by TeamRatingsController; see db/AddRatingsSystem.sql).
/// </summary>
public class SetRatingRequest
{
    public int BookingId { get; set; }
    public int RaterUserId { get; set; }
    public int RateeUserId { get; set; }
    public string RateeRoleCode { get; set; } = string.Empty;
    public int Stars { get; set; }
    public string? Comment { get; set; }
}
