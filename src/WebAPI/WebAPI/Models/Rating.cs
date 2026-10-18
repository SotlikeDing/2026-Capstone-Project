using System.Text.Json.Serialization;

namespace WebAPI.Models;

/// <summary>A single star rating row (ratings table).</summary>
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

/// <summary>Payload for submitting (or correcting) a rating.</summary>
public class SetRatingRequest
{
    public int BookingId { get; set; }
    public int RaterUserId { get; set; }
    public int RateeUserId { get; set; }
    public string RateeRoleCode { get; set; } = string.Empty;
    public int Stars { get; set; }
    public string? Comment { get; set; }
}

/// <summary>Raw aggregate from the ratings table only - no booking-count knowledge.</summary>
public class RatingSummaryDto
{
    public decimal AverageStars { get; set; }
    public int RatingCount { get; set; }
}

/// <summary>What the profile/dashboard pages actually display - summary + trip count.</summary>
public class RatingProfileStatsDto
{
    public decimal AverageStars { get; set; }
    public int RatingCount { get; set; }
    public int CompletedTrips { get; set; }
}

/// <summary>One recent rating with rater name and comment, for a reviews list.</summary>
public class RecentRatingDto
{
    public int RatingId { get; set; }
    public string RaterName { get; set; } = string.Empty;
    public int Stars { get; set; }
    public string? Comment { get; set; }
    public DateTime CreatedAt { get; set; }
    public string RoleContext { get; set; } = string.Empty;
}

/// <summary>Used by LandingController's getLeaderboard endpoint.</summary>
public class LeaderboardEntry
{
    public int RateeId { get; set; }
    public decimal AverageStars { get; set; }
}

/// <summary>
/// Who was actually on a booking and whether it's completed - used to validate that
/// a rating is for a real participant on a real, finished trip before it's accepted.
/// Null booking (BookingExists = false) or null role-slot means "not applicable".
/// </summary>
public class BookingParticipantsDto
{
    public bool BookingExists { get; set; }
    public string? Status { get; set; }
    public int? HunterUserId { get; set; }
    public int? PhUserId { get; set; }
    public int? VgUserId { get; set; }
    public DateTime? EndDate { get; set; }
    public string? HunterName { get; set; }
    public string? PhName { get; set; }
    public string? VgName { get; set; }

    private static readonly HashSet<string> ConfirmedStatuses = new(StringComparer.OrdinalIgnoreCase) { "Approved", "Paid" };

    /// <summary>
    /// A trip can be rated once it is marked Completed, or once a confirmed
    /// (Approved/Paid) trip's end date has passed - nothing in the platform flips a
    /// finished trip to Completed automatically, so waiting for that alone would mean
    /// most trips could never be rated.
    /// </summary>
    public bool IsRateable(DateTime today) =>
        string.Equals(Status, "Completed", StringComparison.OrdinalIgnoreCase)
        || (Status != null && ConfirmedStatuses.Contains(Status) && EndDate.HasValue && EndDate.Value.Date < today.Date);
}

/// <summary>One person the caller can rate on a booking, with the caller's current rating of them (if any).</summary>
public class BookingTeamMemberDto
{
    public int UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>HUNTER, PH or VG - the capacity they were on this booking in (sent back as RateeRoleCode).</summary>
    public string RoleCode { get; set; } = string.Empty;
    public int? MyStars { get; set; }
    public string? MyComment { get; set; }
}

/// <summary>The "rate your team" view of one booking for the calling participant.</summary>
public class BookingTeamRatingDto
{
    public int BookingId { get; set; }
    public int CallerUserId { get; set; }
    public bool CanRate { get; set; }
    public string? Reason { get; set; }
    public List<BookingTeamMemberDto> Members { get; set; } = new();
}

/// <summary>The caller's existing rating of one person on one booking.</summary>
public class OwnRatingDto
{
    public int RateeUserId { get; set; }
    public int Stars { get; set; }
    public string? Comment { get; set; }
}

/// <summary>A rating comment flagged for moderation review.</summary>
public class FlaggedRatingCommentDto
{
    public int Id { get; set; }
    public int RatingId { get; set; }
    public int RaterUserId { get; set; }
    public string RaterUserName { get; set; } = string.Empty;
    public int RateeUserId { get; set; }
    public string RateeUserName { get; set; } = string.Empty;
    public string CommentText { get; set; } = string.Empty;
    public string FlaggedKeywords { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string Status { get; set; } = "PendingReview";
}

/// <summary>One entry in a rating's immutable edit trail (see ratingHistory / MERGE...OUTPUT INTO in Rating_db.SetRoleRating).</summary>
public class RatingHistoryEntryDto
{
    public int HistoryId { get; set; }
    public string ChangeType { get; set; } = string.Empty; // 'INSERT' or 'UPDATE'
    public int RatingId { get; set; }
    public int? PreviousStars { get; set; }
    public string? PreviousComment { get; set; }
    public int NewStars { get; set; }
    public string? NewComment { get; set; }
    public DateTime ChangedAt { get; set; }
}

/// <summary>Payload for the ratee disputing a rating they received.</summary>
public class CreateDisputeRequest
{
    public string Reason { get; set; } = string.Empty;
}

/// <summary>Payload for an admin's dispute resolution.</summary>
public class ResolveDisputeRequest
{
    public string Status { get; set; } = string.Empty; // 'Upheld' or 'Denied'
    public string? Notes { get; set; }
}

/// <summary>A rating dispute, with enough context (rater/ratee names, original stars/comment) for an admin to review without a separate lookup.</summary>
public class RatingDisputeDto
{
    public int DisputeId { get; set; }
    public int RatingId { get; set; }
    public int DisputedByUserId { get; set; }
    public string DisputedByUserName { get; set; } = string.Empty;
    public int RaterUserId { get; set; }
    public string RaterUserName { get; set; } = string.Empty;
    public int Stars { get; set; }
    public string? Comment { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string Status { get; set; } = "PendingReview";
    public DateTime CreatedAt { get; set; }
    public int? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ResolutionNotes { get; set; }
}
