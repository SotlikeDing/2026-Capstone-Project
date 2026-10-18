using System.Text.Json;
using System.Text.Json.Serialization;

namespace MobileApi.Models;

public enum MobileRole
{
    Restricted,
    Hunter,
    Ph,
    Vg
}

public static class MobileCapabilityNames
{
    public const string ViewTrophyRoom = "canViewTrophyRoom";
    public const string SubmitTrophy = "canSubmitTrophy";
    public const string ReviewTrophies = "canReviewTrophies";
    public const string ViewBookingMedia = "canViewBookingMedia";
    public const string UploadBookingMedia = "canUploadBookingMedia";
    public const string UploadBookingVideo = "canUploadBookingVideo";
    public const string EditOwnBookingMedia = "canEditOwnBookingMedia";
    public const string ModerateBookingMedia = "canModerateBookingMedia";
    public const string SubmitRoute = "canSubmitRoute";
    public const string SelectRoute = "canSelectRoute";
    public const string GenerateRecap = "canGenerateRecap";
    public const string PublishRecap = "canPublishRecap";
    public const string ViewRecap = "canViewRecap";
}

public sealed record MobileRoleContext(
    int UserId,
    int UserRoleId,
    MobileRole Role,
    string RoleCode,
    IReadOnlySet<string> Capabilities)
{
    public bool IsSupported => Role is MobileRole.Hunter or MobileRole.Ph or MobileRole.Vg;
    public bool Can(string capability) => IsSupported && Capabilities.Contains(capability);
}

public sealed class MobileAccessResponse
{
    public int UserId { get; init; }
    public int UserRoleId { get; init; }
    public string Role { get; init; } = "Restricted";
    public string RoleCode { get; init; } = string.Empty;
    public bool IsSupported { get; init; }
    public string? RestrictionReason { get; init; }
    public IReadOnlyCollection<string> Capabilities { get; init; } = [];
}

public sealed record BookingAccess(
    int BookingId,
    string Title,
    DateTime StartDate,
    DateTime EndDate,
    string Status,
    bool IsCancelled,
    bool IsEligible,
    bool IsParticipant);

public sealed class BookingMediaDto
{
    public long MediaId { get; set; }
    public int BookingId { get; set; }
    public int UploaderUserId { get; set; }
    [JsonIgnore] public int UploaderUserRoleId { get; set; }
    public string UploaderRole { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    /// <summary>"Photo" or "Video" (short VG clip).</summary>
    public string SourceType { get; set; } = "Photo";
    public decimal? DurationSeconds { get; set; }
    public DateTime? OriginalCaptureTimestamp { get; set; }
    public DateTime? AdjustedCaptureTimestamp { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public int? Orientation { get; set; }
    public string? Caption { get; set; }
    public string MatchMethod { get; set; } = "Unmatched";
    public string MatchConfidence { get; set; } = "None";
    public int? MatchedRoutePointIndex { get; set; }
    public string? MatchedPinId { get; set; }
    public string InclusionStatus { get; set; } = "Included";
    public string ReviewStatus { get; set; } = "NotRequired";
    public int MediaVersion { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string DownloadPath { get; set; } = string.Empty;
    [JsonIgnore] public string PrivateStorageReference { get; set; } = string.Empty;
}

public sealed class BookingMediaUpdateRequest
{
    public string? Caption { get; set; }
    public DateTime? AdjustedCaptureTimestamp { get; set; }
    public int? MatchedRoutePointIndex { get; set; }
    public string? MatchedPinId { get; set; }
    public string? MatchMethod { get; set; }
}

public sealed class BookingMediaModerationRequest
{
    public string InclusionStatus { get; set; } = string.Empty;
    public string? Reason { get; set; }
}

public sealed class RoutePointRequest
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public DateTime Timestamp { get; set; }
}

public sealed class RoutePinRequest
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public DateTime Timestamp { get; set; }
    public string? Note { get; set; }
}

public sealed class SubmitBookingRouteRequest
{
    public string SourceHuntSessionId { get; set; } = string.Empty;
    public List<RoutePointRequest> Points { get; set; } = [];
    public List<RoutePinRequest> Pins { get; set; } = [];
    public double DistanceMeters { get; set; }
    public int DurationSeconds { get; set; }
    public int RouteVersion { get; set; } = 1;
}

public sealed class BookingRouteDto
{
    public long RouteId { get; set; }
    public int BookingId { get; set; }
    public int OwnerUserId { get; set; }
    public string SourceHuntSessionId { get; set; } = string.Empty;
    public List<RoutePointRequest> Points { get; set; } = [];
    public List<RoutePinRequest> Pins { get; set; } = [];
    public int PointCount { get; set; }
    public decimal DistanceMeters { get; set; }
    public int DurationSeconds { get; set; }
    public int RouteVersion { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime SubmittedAt { get; set; }
    public bool SelectedForRecap { get; set; }

    public static List<RoutePointRequest> ParsePoints(string json) =>
        JsonSerializer.Deserialize<List<RoutePointRequest>>(json, JsonDefaults.Options) ?? [];
    public static List<RoutePinRequest> ParsePins(string? json) => string.IsNullOrWhiteSpace(json)
        ? []
        : JsonSerializer.Deserialize<List<RoutePinRequest>>(json, JsonDefaults.Options) ?? [];
}

public sealed class RequestRecapRequest
{
    public string? Title { get; set; }
}

public sealed class BookingRecapDto
{
    public long RecapId { get; set; }
    public int BookingId { get; set; }
    public long SelectedRouteId { get; set; }
    public string Status { get; set; } = string.Empty;
    public int Progress { get; set; }
    public int SourceMediaVersion { get; set; }
    public long? OutputFileSize { get; set; }
    public int? DurationSeconds { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? FailureReason { get; set; }
    public int Version { get; set; }
    public string UploadStatus { get; set; } = "NotRequested";
    public int UploadProgress { get; set; }
    public string DeletionStatus { get; set; } = "NotRequested";
    public string? YoutubeUrl { get; set; }
    public string? YoutubePrivacyStatus { get; set; }
    public string? YoutubeProcessingStatus { get; set; }
    public DateTime? YoutubeVerifiedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? PlaybackPath { get; set; }
    [JsonIgnore] public DateTime BookingStartDate { get; set; }
    [JsonIgnore] public DateTime BookingEndDate { get; set; }
    [JsonIgnore] public string? OutputPrivateStorageReference { get; set; }
}

public sealed class SubmitTrophyRequest
{
    public int BookingId { get; set; }
    public string Species { get; set; } = string.Empty;
    public string TrophyPhotoPath { get; set; } = string.Empty;
    public decimal? AntlerSizeCm { get; set; }
    public decimal? CarcassWeightKg { get; set; }
    public DateTime HarvestDate { get; set; }
    public string LocationText { get; set; } = string.Empty;
    public int? ShotDistanceMeters { get; set; }
    public string FirearmType { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public List<long> SupportingMediaIds { get; set; } = [];
    public MobileApi.Models.RowlandWard.RowlandWardEvaluationRequest? RowlandWard { get; set; }
}

public sealed class TrophySubmissionDto
{
    public long SubmissionId { get; set; }
    public int BookingId { get; set; }
    public string BookingTitle { get; set; } = string.Empty;
    public int HunterUserId { get; set; }
    public string HunterName { get; set; } = string.Empty;
    public string Species { get; set; } = string.Empty;
    public string TrophyPhotoPath { get; set; } = string.Empty;
    [JsonIgnore] public string TrophyPhotoStorageReference { get; set; } = string.Empty;
    public decimal? AntlerSizeCm { get; set; }
    public decimal? CarcassWeightKg { get; set; }
    public DateTime HarvestDate { get; set; }
    public string LocationText { get; set; } = string.Empty;
    public int? ShotDistanceMeters { get; set; }
    public string FirearmType { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public List<long> SupportingMediaIds { get; set; } = [];
    public string Status { get; set; } = string.Empty;
    public string? ReviewNotes { get; set; }
    public int SupportingPhotoCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class TrophySubmissionRecord
{
    public TrophySubmissionDto Submission { get; set; } = new();
    public MobileApi.Models.RowlandWard.RowlandWardEvaluationRequest? RowlandWard { get; set; }
}

public sealed class TrophyReviewRequest
{
    public string Action { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public Guid ActionKey { get; set; }
}

public sealed class TrophyResubmitRequest
{
    public string? Notes { get; set; }
    public string? TrophyPhotoPath { get; set; }
}

public sealed class PublishRecapRequest
{
    public Guid IdempotencyKey { get; set; }
    public string PrivacyStatus { get; set; } = "private";
}

public sealed class CleanupRequest
{
    public bool ConfirmImmediateCleanup { get; set; }
    public Guid IdempotencyKey { get; set; }
}

internal static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
