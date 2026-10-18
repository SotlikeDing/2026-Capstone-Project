namespace MobileApi.Options;

public sealed class SharedBookingOptions
{
    public const string SectionName = "SharedBooking";
    public string StoragePath { get; set; } = Path.Combine("Data", "SharedBooking");
    public int MaxPhotoBytes { get; set; } = 15 * 1024 * 1024;
    public int MaxPhotosPerBooking { get; set; } = 500;
    public int MaxPhotosPerUploader { get; set; } = 150;
    public long MaxBookingMediaBytes { get; set; } = 3L * 1024 * 1024 * 1024;
    public int MaxRoutePoints { get; set; } = 50_000;
    public int MaxRoutePayloadBytes { get; set; } = 8 * 1024 * 1024;
    public int MaxVideoDurationSeconds { get; set; } = 20 * 60;
    public long MaxGeneratedVideoBytes { get; set; } = 4L * 1024 * 1024 * 1024;
    public long MinimumTemporaryFreeBytes { get; set; } = 6L * 1024 * 1024 * 1024;
    public int MaxRecapJobsPerBooking { get; set; } = 10;
    public int MaxYoutubeRetryAttempts { get; set; } = 8;
    public int CleanupRetentionHours { get; set; } = 48;
    public string FfmpegExecutable { get; set; } = "ffmpeg";
    public string FfprobeExecutable { get; set; } = "ffprobe";
    public long MaxVideoClipBytes { get; set; } = 100L * 1024 * 1024;
    public int MaxVideoClipSeconds { get; set; } = 30;
    public int MaxVideosPerUploader { get; set; } = 20;
}

/// <summary>
/// Shared secret that lets the trusted WebAPI call the booking media endpoints on
/// behalf of an already-authenticated website user. Empty disables the scheme.
/// </summary>
public sealed class InternalApiOptions
{
    public const string SectionName = "InternalApi";
    public string Key { get; set; } = string.Empty;
    public string WebApiBaseUrl { get; set; } = string.Empty;
}

public sealed class YoutubeOptions
{
    public const string SectionName = "Youtube";
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = string.Empty;
    public string DataProtectionKeysPath { get; set; } = Path.Combine("Data", "YoutubeKeys");
    public string DataProtectionPurpose { get; set; } = "Mikpunt.YouTube.RefreshTokens.v1";
    public string ExpectedPrivacyStatus { get; set; } = "private";
}
