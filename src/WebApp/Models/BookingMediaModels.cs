namespace WebApp.Models
{
    // Client-side mirrors of MobileAPI's BookingMediaDto / BookingRecapDto (relayed by
    // WebAPI's VgBookingMediaController) and WebAPI's BookingTeamRatingDto (camelCase JSON).

    /// <summary>One photo or short video clip a participant added to a finished booking.</summary>
    public class BookingMediaItemModel
    {
        public long MediaId { get; set; }
        public int BookingId { get; set; }
        public int UploaderUserId { get; set; }
        public string UploaderRole { get; set; } = string.Empty;
        public string OriginalFileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public long FileSizeBytes { get; set; }
        /// <summary>"Photo" or "Video".</summary>
        public string SourceType { get; set; } = "Photo";
        public decimal? DurationSeconds { get; set; }
        public DateTime? OriginalCaptureTimestamp { get; set; }
        public DateTime? AdjustedCaptureTimestamp { get; set; }
        public string? Caption { get; set; }
        public string MatchMethod { get; set; } = "Unmatched";
        /// <summary>"Included" or "Excluded" - the PH decides which media go into the story.</summary>
        public string InclusionStatus { get; set; } = "Included";
        public DateTime CreatedAt { get; set; }

        public bool IsVideo => string.Equals(SourceType, "Video", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The generated hunt story (recap video) for a booking.</summary>
    public class BookingRecapSummaryModel
    {
        public long RecapId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int Progress { get; set; }
        public string UploadStatus { get; set; } = string.Empty;
        public string? FailureReason { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
    }

    /// <summary>A person the signed-in participant can rate on a booking.</summary>
    public class BookingTeamMemberModel
    {
        public int UserId { get; set; }
        public string Name { get; set; } = string.Empty;
        /// <summary>HUNTER, PH or VG.</summary>
        public string RoleCode { get; set; } = string.Empty;
        public int? MyStars { get; set; }
        public string? MyComment { get; set; }
    }

    /// <summary>GET api/rating/booking/{id}/team.</summary>
    public class BookingTeamRatingModel
    {
        public int BookingId { get; set; }
        public int CallerUserId { get; set; }
        public bool CanRate { get; set; }
        public string? Reason { get; set; }
        public List<BookingTeamMemberModel> Members { get; set; } = new();
    }
}
