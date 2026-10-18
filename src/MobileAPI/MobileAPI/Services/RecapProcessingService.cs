using System.Data;
using System.Diagnostics;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using MobileApi.Database;
using MobileApi.Models;
using MobileApi.Options;

namespace MobileApi.Services;

public sealed record ClaimedRecapJob(long JobId, long RecapId);

public interface IRecapJobStore
{
    Task<ClaimedRecapJob?> ClaimAsync(string workerId, CancellationToken cancellationToken);
    Task SetProgressAsync(ClaimedRecapJob job, int progress, CancellationToken cancellationToken);
    Task CompleteAsync(ClaimedRecapJob job, string storageReference, long size, int durationSeconds, string? warning, CancellationToken cancellationToken);
    Task FailAsync(ClaimedRecapJob job, string reason, CancellationToken cancellationToken);
}

public sealed class RecapJobStore : IRecapJobStore
{
    private readonly string _connectionString;
    public RecapJobStore(IConfiguration configuration) => _connectionString = configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Connection string 'DefaultConnection' was not found.");

    public async Task<ClaimedRecapJob?> ClaimAsync(string workerId, CancellationToken cancellationToken)
    {
        const string sql = """
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;
            DECLARE @JobId bigint, @RecapId bigint;
            SELECT TOP 1 @JobId = recapProcessingJobId, @RecapId = recapId
            FROM dbo.recapProcessingJobs WITH (UPDLOCK, READPAST, ROWLOCK)
            WHERE (status = 'Queued' OR (status = 'Processing' AND leaseExpiresAt < SYSUTCDATETIME()))
              AND nextAttemptAt <= SYSUTCDATETIME()
            ORDER BY createdAt;
            IF @JobId IS NOT NULL
            BEGIN
                UPDATE dbo.recapProcessingJobs SET status = 'Processing', leaseOwner = @Worker,
                    leaseExpiresAt = DATEADD(minute, 10, SYSUTCDATETIME()), attemptCount = attemptCount + 1,
                    updatedAt = SYSUTCDATETIME() WHERE recapProcessingJobId = @JobId;
                UPDATE dbo.bookingRecaps SET status = 'Processing', startedAt = COALESCE(startedAt, SYSUTCDATETIME()) WHERE recapId = @RecapId;
            END;
            COMMIT;
            SELECT @JobId, @RecapId;
            """;
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Worker", SqlDbType.NVarChar, 120).Value = workerId;
        await connection.OpenAsync(cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken) || reader.IsDBNull(0)) return null;
        return new ClaimedRecapJob(reader.GetInt64(0), reader.GetInt64(1));
    }

    public Task SetProgressAsync(ClaimedRecapJob job, int progress, CancellationToken cancellationToken) => ExecuteAsync("""
        UPDATE dbo.bookingRecaps SET progress = @Progress WHERE recapId = @RecapId AND status = 'Processing';
        UPDATE dbo.recapProcessingJobs SET leaseExpiresAt = DATEADD(minute, 10, SYSUTCDATETIME()), updatedAt = SYSUTCDATETIME()
        WHERE recapProcessingJobId = @JobId AND status = 'Processing';
        """, job, cancellationToken, Math.Clamp(progress, 0, 99));

    public Task CompleteAsync(ClaimedRecapJob job, string storageReference, long size, int durationSeconds, string? warning, CancellationToken cancellationToken) => ExecuteAsync("""
        SET XACT_ABORT ON; BEGIN TRANSACTION;
        UPDATE dbo.bookingRecaps SET status = 'Completed', progress = 100, outputPrivateStorageReference = @Storage,
            outputFileSize = @Size, durationSeconds = @Duration, completedAt = SYSUTCDATETIME(), failureReason = @Warning
        WHERE recapId = @RecapId AND status = 'Processing';
        UPDATE dbo.recapProcessingJobs SET status = 'Completed', leaseOwner = NULL, leaseExpiresAt = NULL,
            lastError = @Warning, updatedAt = SYSUTCDATETIME() WHERE recapProcessingJobId = @JobId;
        COMMIT;
        """, job, cancellationToken, storage: storageReference, size: size, duration: durationSeconds, error: warning);

    public Task FailAsync(ClaimedRecapJob job, string reason, CancellationToken cancellationToken) => ExecuteAsync("""
        SET XACT_ABORT ON; BEGIN TRANSACTION;
        UPDATE dbo.bookingRecaps SET status = 'Failed', failureReason = @Error WHERE recapId = @RecapId;
        UPDATE dbo.recapProcessingJobs SET status = 'Failed', leaseOwner = NULL, leaseExpiresAt = NULL,
            lastError = @Error, updatedAt = SYSUTCDATETIME() WHERE recapProcessingJobId = @JobId;
        COMMIT;
        """, job, cancellationToken, error: reason);

    private async Task ExecuteAsync(string sql, ClaimedRecapJob job, CancellationToken cancellationToken, int? progress = null,
        string? storage = null, long? size = null, int? duration = null, string? error = null)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@JobId", SqlDbType.BigInt).Value = job.JobId;
        command.Parameters.Add("@RecapId", SqlDbType.BigInt).Value = job.RecapId;
        if (sql.Contains("@Progress")) command.Parameters.Add("@Progress", SqlDbType.Int).Value = progress ?? 0;
        if (sql.Contains("@Storage")) command.Parameters.Add("@Storage", SqlDbType.NVarChar, 700).Value = storage!;
        if (sql.Contains("@Size")) command.Parameters.Add("@Size", SqlDbType.BigInt).Value = size!.Value;
        if (sql.Contains("@Duration")) command.Parameters.Add("@Duration", SqlDbType.Int).Value = duration!.Value;
        if (sql.Contains("@Error")) command.Parameters.Add("@Error", SqlDbType.NVarChar, 1000).Value = (object?)error ?? DBNull.Value;
        if (sql.Contains("@Warning")) command.Parameters.Add("@Warning", SqlDbType.NVarChar, 1000).Value = (object?)error ?? DBNull.Value;
        await connection.OpenAsync(cancellationToken);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

public sealed record RecapRenderResult(string StorageReference, long Size, int DurationSeconds, int SkippedPhotos);

public interface IRecapRenderer
{
    Task<RecapRenderResult> RenderAsync(BookingRecapDto recap, BookingRouteDto route, IReadOnlyList<BookingMediaDto> media,
        Func<int, Task> reportProgress, CancellationToken cancellationToken);
}

/// <summary>
/// Produces one deterministic MP4 from a projected route animation followed by
/// all eligible participant photos. Every photo is decoded by FFmpeg, scaled,
/// and re-encoded with metadata removed before it can enter the recap.
/// </summary>
public sealed class FfmpegRecapRenderer : IRecapRenderer
{
    private const int Width = 1280;
    private const int Height = 720;
    private const int FramesPerSecond = 25;
    private const double RouteFrameSeconds = 0.10;
    private const double LabelSeconds = 0.8;
    private const double PhotoSeconds = 2.0;

    /// <summary>Normalises any section (stills or a phone clip, portrait or landscape) to the recap's 720p canvas.</summary>
    private static readonly string SectionFilter =
        $"scale={Width}:{Height}:force_original_aspect_ratio=decrease,pad={Width}:{Height}:(ow-iw)/2:(oh-ih)/2:color=black," +
        $"setsar=1,fps={FramesPerSecond},format=yuv420p";

    /// <summary>
    /// Every section uses exactly these encoder settings so the final join can copy
    /// the streams instead of re-encoding the whole recap. Audio is always dropped.
    /// </summary>
    private static readonly string[] SectionEncoding =
    [
        "-an", "-sn", "-dn", "-map_metadata", "-1",
        "-c:v", "libx264", "-preset", "medium", "-profile:v", "high", "-level:v", "4.0",
        "-pix_fmt", "yuv420p", "-r", "25", "-video_track_timescale", "12800"
    ];
    private readonly ISharedBookingStorage _storage;
    private readonly SharedBookingOptions _options;

    public FfmpegRecapRenderer(ISharedBookingStorage storage, IOptions<SharedBookingOptions> options)
    { _storage = storage; _options = options.Value; }

    public async Task<RecapRenderResult> RenderAsync(BookingRecapDto recap, BookingRouteDto route, IReadOnlyList<BookingMediaDto> media,
        Func<int, Task> reportProgress, CancellationToken cancellationToken)
    {
        var routeFrames = Math.Clamp(route.Points.Count, 20, 120);
        var photoCount = media.Count(item => !IsVideo(item));
        var clipSeconds = media.Where(IsVideo).Sum(item => ClipSeconds(item) + LabelSeconds);
        var estimatedDuration = 5 + routeFrames * RouteFrameSeconds + photoCount * (LabelSeconds + PhotoSeconds) + clipSeconds;
        if (estimatedDuration > _options.MaxVideoDurationSeconds)
            throw new InvalidOperationException("Die beplande recap oorskry die maksimum videolengte.");
        var inputBytes = media.Sum(item => Math.Max(0, item.FileSizeBytes));
        var frameBytes = (long)(routeFrames + media.Count * 2 + 4) * Width * Height * 3;
        var estimatedWorkingBytes = inputBytes * 2 + frameBytes + Math.Min(inputBytes, _options.MaxGeneratedVideoBytes);
        if (_storage.GetAvailableBytes() < _options.MinimumTemporaryFreeBytes + estimatedWorkingBytes)
            throw new InvalidOperationException("Daar is nie genoeg veilige tydelike berging om die recap te genereer nie.");
        var temporary = Path.Combine(Path.GetTempPath(), $"mikpunt-recap-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporary);
        try
        {
            // The recap is assembled from sections that are all encoded with identical
            // settings, so they can be joined without a second encode: runs of still
            // frames (intro, route, photos, summary) and each VG clip as its own section.
            var sections = new List<string>();
            var stills = new List<(string Path, double Seconds)>();
            var frameIndex = 0;
            var totalSeconds = 0d;
            string NextFrame() => Path.Combine(temporary, $"frame-{frameIndex++:D5}.ppm");

            async Task FlushStillsAsync()
            {
                if (stills.Count == 0) return;
                var section = Path.Combine(temporary, $"section-{sections.Count:D4}.mp4");
                var listPath = Path.Combine(temporary, $"section-{sections.Count:D4}.txt");
                var list = new StringBuilder();
                foreach (var (path, seconds) in stills) Append(list, path, seconds);
                // The concat demuxer ignores the final entry's duration unless the file is listed once more.
                list.Append("file '").Append(Path.GetFileName(stills[^1].Path)).AppendLine("'");
                await File.WriteAllTextAsync(listPath, list.ToString(), new UTF8Encoding(false), cancellationToken);
                await RunAsync(_options.FfmpegExecutable, temporary, cancellationToken,
                    ["-hide_banner", "-loglevel", "error", "-y", "-f", "concat", "-safe", "1", "-i", listPath,
                     "-vf", SectionFilter, .. SectionEncoding, section]);
                totalSeconds += stills.Sum(x => x.Seconds);
                sections.Add(section);
                stills.Clear();
            }

            var intro = NextFrame();
            WriteIntroFrame(intro, recap);
            stills.Add((intro, 2.0));

            for (var index = 1; index <= routeFrames; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = NextFrame();
                WriteRouteFrame(path, route, index / (double)routeFrames);
                stills.Add((path, RouteFrameSeconds));
            }
            await reportProgress(20);

            var skipped = 0;
            var included = 0;
            var includedMedia = new List<BookingMediaDto>();
            foreach (var item in media)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!_storage.TryGetPhoto(item.PrivateStorageReference, recap.BookingId, out var source) || source is null)
                { skipped++; continue; }
                var labelPath = NextFrame();
                var captured = item.AdjustedCaptureTimestamp ?? item.OriginalCaptureTimestamp;
                WriteMomentFrame(labelPath, item, captured);
                if (IsVideo(item))
                {
                    var clipSection = Path.Combine(temporary, $"clip-{item.MediaId}.mp4");
                    try
                    {
                        // Video only: clip audio (wind, gunshots, chatter) never enters the recap.
                        await RunAsync(_options.FfmpegExecutable, temporary, cancellationToken,
                            ["-hide_banner", "-loglevel", "error", "-y", "-i", source.FullPath,
                             "-t", ClipSeconds(item).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
                             "-map", "0:v:0", "-vf", SectionFilter, .. SectionEncoding, clipSection]);
                        stills.Add((labelPath, LabelSeconds));
                        await FlushStillsAsync();
                        sections.Add(clipSection);
                        totalSeconds += ClipSeconds(item);
                        included++;
                        includedMedia.Add(item);
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    { skipped++; TryDelete(labelPath); TryDelete(clipSection); }
                }
                else
                {
                    var path = NextFrame();
                    try
                    {
                        await RunAsync(_options.FfmpegExecutable, temporary, cancellationToken,
                            "-hide_banner", "-loglevel", "error", "-y", "-i", source.FullPath,
                            "-map_metadata", "-1", "-vf", $"scale={Width}:{Height}:force_original_aspect_ratio=decrease,pad={Width}:{Height}:(ow-iw)/2:(oh-ih)/2:color=black",
                            "-frames:v", "1", path);
                        stills.Add((labelPath, LabelSeconds));
                        stills.Add((path, PhotoSeconds));
                        included++;
                        includedMedia.Add(item);
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    { skipped++; TryDelete(labelPath); TryDelete(path); }
                }
                await reportProgress(20 + (int)(60d * (included + skipped) / Math.Max(1, media.Count)));
            }
            if (included == 0) throw new InvalidOperationException("Geen geldige foto's of videogrepe was beskikbaar vir die recap nie.");

            var summary = NextFrame();
            WriteSummaryFrame(summary, includedMedia, route);
            stills.Add((summary, 3.0));
            await FlushStillsAsync();
            await reportProgress(85);

            var joinList = new StringBuilder();
            foreach (var section in sections)
                joinList.Append("file '").Append(Path.GetFileName(section)).AppendLine("'");
            var joinPath = Path.Combine(temporary, "sections.txt");
            await File.WriteAllTextAsync(joinPath, joinList.ToString(), new UTF8Encoding(false), cancellationToken);

            var outputPath = _storage.GetRecapOutputPath(recap.BookingId, recap.RecapId);
            await RunAsync(_options.FfmpegExecutable, temporary, cancellationToken,
                "-hide_banner", "-loglevel", "error", "-y", "-f", "concat", "-safe", "1", "-i", joinPath,
                "-map", "0:v:0", "-c", "copy", "-map_metadata", "-1", "-movflags", "+faststart", outputPath);
            await reportProgress(95);
            var info = new FileInfo(outputPath);
            if (!info.Exists || info.Length <= 0 || info.Length > _options.MaxGeneratedVideoBytes)
            { TryDelete(outputPath); throw new InvalidOperationException("Die gegenereerde videolêer is ongeldig of te groot."); }
            var duration = (int)Math.Ceiling(totalSeconds);
            if (duration > _options.MaxVideoDurationSeconds)
            { TryDelete(outputPath); throw new InvalidOperationException("Die recap oorskry die maksimum videolengte."); }
            return new RecapRenderResult(_storage.ToRecapStorageReference(recap.BookingId, recap.RecapId), info.Length, duration, skipped);
        }
        finally
        {
            try { if (Directory.Exists(temporary)) Directory.Delete(temporary, recursive: true); }
            catch { /* maintenance can remove an abandoned, uniquely-scoped temp directory */ }
        }
    }

    /// <summary>South Africa has no daylight saving: SAST is always UTC+2.</summary>
    private static readonly TimeSpan SouthAfricaOffset = TimeSpan.FromHours(2);

    private static string RoleLabel(string role) => role switch { "Hunter" => "JAGTER", _ => role };

    private static bool IsVideo(BookingMediaDto item) => string.Equals(item.SourceType, "Video", StringComparison.OrdinalIgnoreCase);

    /// <summary>Seconds of a clip that make it into the recap - never more than the per-clip limit.</summary>
    private double ClipSeconds(BookingMediaDto item) =>
        Math.Min((double)(item.DurationSeconds ?? _options.MaxVideoClipSeconds), _options.MaxVideoClipSeconds);

    private static void Append(StringBuilder builder, string path, double duration)
    {
        builder.Append("file '").Append(Path.GetFileName(path).Replace("'", "''", StringComparison.Ordinal)).AppendLine("'");
        builder.Append("duration ").AppendLine(duration.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
    }

    private static async Task RunAsync(string executable, string workingDirectory, CancellationToken cancellationToken, params string[] arguments)
    {
        var start = new ProcessStartInfo(executable) { WorkingDirectory = workingDirectory, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("FFmpeg kon nie begin nie.");
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch { }
            throw;
        }
        _ = await errorTask;
        if (process.ExitCode != 0)
            throw new InvalidOperationException("FFmpeg kon een van die private mediabates nie veilig verwerk nie.");
    }

    private static void WriteIntroFrame(string path, BookingRecapDto recap)
    {
        var canvas = new PpmCanvas(Width, Height, 34, 31, 24);
        canvas.DrawText(recap.Title, 80, 220, 8, 226, 192, 112);
        canvas.DrawText("GEDEELDE JAGSTORIE", 80, 335, 5, 225, 220, 205);
        canvas.DrawText(
            $"{recap.BookingStartDate:yyyy-MM-dd} - {recap.BookingEndDate:yyyy-MM-dd}",
            80,
            420,
            4,
            225,
            220,
            205);
        canvas.Save(path);
    }

    private static void WriteMomentFrame(string path, BookingMediaDto item, DateTime? captured)
    {
        var canvas = new PpmCanvas(Width, Height, 27, 38, 27);
        canvas.DrawText($"{RoleLabel(item.UploaderRole)} {(IsVideo(item) ? "VIDEO" : "FOTO")}", 70, 190, 7, 226, 192, 112);
        canvas.DrawText(
            captured is { } at ? at.ToUniversalTime().Add(SouthAfricaOffset).ToString("yyyy-MM-dd HH:mm") : "TYD ONBEKEND",
            70,
            305,
            4,
            225,
            220,
            205);
        canvas.DrawText(
            string.IsNullOrWhiteSpace(item.Caption) ? "JAGOOMBLIK" : item.Caption,
            70,
            390,
            4,
            225,
            220,
            205);
        canvas.Save(path);
    }

    private static void WriteSummaryFrame(
        string path,
        IReadOnlyList<BookingMediaDto> media,
        BookingRouteDto route)
    {
        var hunter = media.Count(x => x.UploaderRole == "Hunter");
        var ph = media.Count(x => x.UploaderRole == "PH");
        var vg = media.Count(x => x.UploaderRole == "VG");
        var clips = media.Count(IsVideo);
        var photos = media.Count - clips;
        var canvas = new PpmCanvas(Width, Height, 34, 31, 24);
        canvas.DrawText(clips == 0 ? $"{photos} FOTO'S" : $"{photos} FOTO'S {clips} GREPE", 80, 210, clips == 0 ? 8 : 7, 226, 192, 112);
        canvas.DrawText($"{route.DistanceMeters / 1000m:0.0} KM ROETE", 80, 330, 5, 225, 220, 205);
        canvas.DrawText($"{hunter} JAGTER  {ph} PH  {vg} VG", 80, 420, 4, 225, 220, 205);
        canvas.DrawText($"{route.Pins.Count} JAGPENNE", 80, 500, 4, 170, 180, 160);
        canvas.Save(path);
    }

    private static void WriteRouteFrame(string path, BookingRouteDto route, double fraction)
    {
        var canvas = new PpmCanvas(Width, Height, 27, 38, 27);
        var points = route.Points;
        var minLat = points.Min(x => x.Latitude); var maxLat = points.Max(x => x.Latitude);
        var minLon = points.Min(x => x.Longitude); var maxLon = points.Max(x => x.Longitude);
        var visible = Math.Clamp((int)Math.Ceiling(points.Count * fraction), 2, points.Count);
        (int X, int Y) Project(RoutePointRequest point)
        {
            var x = 70 + (int)((point.Longitude - minLon) / Math.Max(.000001, maxLon - minLon) * (Width - 140));
            var y = 70 + (int)((maxLat - point.Latitude) / Math.Max(.000001, maxLat - minLat) * (Height - 140));
            return (x, y);
        }
        for (var index = 1; index < visible; index++)
        {
            var a = Project(points[index - 1]); var b = Project(points[index]);
            canvas.Line(a.X, a.Y, b.X, b.Y, 222, 175, 73, 5);
        }
        foreach (var pin in route.Pins)
        {
            var p = Project(new RoutePointRequest { Latitude = pin.Latitude, Longitude = pin.Longitude });
            canvas.Circle(p.X, p.Y, 8, 180, 74, 55);
        }
        canvas.DrawText("JAGROETE", 42, 30, 4, 226, 192, 112);
        canvas.Save(path);
    }

    private static void TryDelete(string path) { try { File.Delete(path); } catch { } }

    /// <summary>
    /// Maps caption/title text onto the card font: upper case, Afrikaans/accented letters
    /// reduced to their base letter (ê -> E, ’n -> 'N) so they don't render as '?'.
    /// Anything still unsupported is drawn as '?' by the font itself.
    /// </summary>
    internal static string ToFontText(string text)
    {
        var decomposed = text.Replace('’', '\'').Replace('‘', '\'').Replace('`', '\'')
            .Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
                builder.Append(c);
        return builder.ToString().ToUpperInvariant();
    }

    private sealed class PpmCanvas
    {
        private readonly int _width; private readonly int _height; private readonly byte[] _pixels;
        public PpmCanvas(int width, int height, byte r, byte g, byte b)
        { _width = width; _height = height; _pixels = new byte[width * height * 3]; for (var i = 0; i < _pixels.Length; i += 3) { _pixels[i] = r; _pixels[i + 1] = g; _pixels[i + 2] = b; } }
        public void Line(int x0, int y0, int x1, int y1, byte r, byte g, byte b, int thickness)
        {
            var dx = Math.Abs(x1 - x0); var sx = x0 < x1 ? 1 : -1; var dy = -Math.Abs(y1 - y0); var sy = y0 < y1 ? 1 : -1; var error = dx + dy;
            while (true) { Circle(x0, y0, thickness / 2, r, g, b); if (x0 == x1 && y0 == y1) break; var twice = 2 * error; if (twice >= dy) { error += dy; x0 += sx; } if (twice <= dx) { error += dx; y0 += sy; } }
        }
        public void Circle(int cx, int cy, int radius, byte r, byte g, byte b)
        { for (var y = -radius; y <= radius; y++) for (var x = -radius; x <= radius; x++) if (x * x + y * y <= radius * radius) Pixel(cx + x, cy + y, r, g, b); }
        public void DrawText(string text, int x, int y, int scale, byte r, byte g, byte b)
        {
            foreach (var raw in ToFontText(text).Take(42))
            {
                var glyph = Font.TryGetValue(raw, out var found) ? found : Font['?'];
                for (var row = 0; row < 7; row++) for (var col = 0; col < 5; col++) if ((glyph[row] & (1 << (4 - col))) != 0)
                    for (var sy = 0; sy < scale; sy++) for (var sx = 0; sx < scale; sx++) Pixel(x + col * scale + sx, y + row * scale + sy, r, g, b);
                x += 6 * scale;
            }
        }
        public void Save(string path)
        { using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write); var header = Encoding.ASCII.GetBytes($"P6\n{_width} {_height}\n255\n"); stream.Write(header); stream.Write(_pixels); }
        private void Pixel(int x, int y, byte r, byte g, byte b) { if (x < 0 || y < 0 || x >= _width || y >= _height) return; var index = (y * _width + x) * 3; _pixels[index] = r; _pixels[index + 1] = g; _pixels[index + 2] = b; }
        private static readonly Dictionary<char, byte[]> Font = BuildFont();
        private static Dictionary<char, byte[]> BuildFont()
        {
            var map = new Dictionary<char, byte[]>();
            void Add(char c, params string[] rows) => map[c] = rows.Select(row => Convert.ToByte(row, 2)).ToArray();
            Add(' ', "00000","00000","00000","00000","00000","00000","00000"); Add('?',"11110","00001","00010","00100","00100","00000","00100");
            var patterns = new Dictionary<char,string[]> {
                ['A']=["01110","10001","10001","11111","10001","10001","10001"], ['B']=["11110","10001","10001","11110","10001","10001","11110"],
                ['C']=["01111","10000","10000","10000","10000","10000","01111"], ['D']=["11110","10001","10001","10001","10001","10001","11110"],
                ['E']=["11111","10000","10000","11110","10000","10000","11111"], ['F']=["11111","10000","10000","11110","10000","10000","10000"],
                ['G']=["01111","10000","10000","10111","10001","10001","01111"], ['H']=["10001","10001","10001","11111","10001","10001","10001"],
                ['I']=["11111","00100","00100","00100","00100","00100","11111"], ['J']=["00111","00010","00010","00010","10010","10010","01100"],
                ['K']=["10001","10010","10100","11000","10100","10010","10001"], ['L']=["10000","10000","10000","10000","10000","10000","11111"],
                ['M']=["10001","11011","10101","10101","10001","10001","10001"], ['N']=["10001","11001","10101","10011","10001","10001","10001"],
                ['O']=["01110","10001","10001","10001","10001","10001","01110"], ['P']=["11110","10001","10001","11110","10000","10000","10000"],
                ['Q']=["01110","10001","10001","10001","10101","10010","01101"], ['R']=["11110","10001","10001","11110","10100","10010","10001"],
                ['S']=["01111","10000","10000","01110","00001","00001","11110"], ['T']=["11111","00100","00100","00100","00100","00100","00100"],
                ['U']=["10001","10001","10001","10001","10001","10001","01110"], ['V']=["10001","10001","10001","10001","10001","01010","00100"],
                ['W']=["10001","10001","10001","10101","10101","11011","10001"], ['X']=["10001","10001","01010","00100","01010","10001","10001"],
                ['Y']=["10001","10001","01010","00100","00100","00100","00100"], ['Z']=["11111","00001","00010","00100","01000","10000","11111"],
                ['0']=["01110","10001","10011","10101","11001","10001","01110"], ['1']=["00100","01100","00100","00100","00100","00100","01110"],
                ['2']=["01110","10001","00001","00010","00100","01000","11111"], ['3']=["11110","00001","00001","01110","00001","00001","11110"],
                ['4']=["00010","00110","01010","10010","11111","00010","00010"], ['5']=["11111","10000","10000","11110","00001","00001","11110"],
                ['6']=["01110","10000","10000","11110","10001","10001","01110"], ['7']=["11111","00001","00010","00100","01000","01000","01000"],
                ['8']=["01110","10001","10001","01110","10001","10001","01110"], ['9']=["01110","10001","10001","01111","00001","00001","01110"],
                ['.']=["00000","00000","00000","00000","00000","00110","00110"], ['-']=["00000","00000","00000","11111","00000","00000","00000"],
                ['!']=["00100","00100","00100","00100","00100","00000","00100"], [',']=["00000","00000","00000","00000","00110","00100","01000"],
                [':']=["00000","00110","00110","00000","00110","00110","00000"], ['\'']=["00100","00100","01000","00000","00000","00000","00000"],
                ['/']=["00001","00010","00010","00100","01000","01000","10000"] };
            foreach (var pair in patterns) Add(pair.Key, pair.Value); return map;
        }
    }
}

public sealed class RecapProcessingWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RecapProcessingWorker> _logger;
    private readonly string _workerId = $"{Environment.MachineName}-{Guid.NewGuid():N}";
    public RecapProcessingWorker(IServiceScopeFactory scopeFactory, ILogger<RecapProcessingWorker> logger) { _scopeFactory = scopeFactory; _logger = logger; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var jobs = scope.ServiceProvider.GetRequiredService<IRecapJobStore>();
                var job = await jobs.ClaimAsync(_workerId, stoppingToken);
                if (job is null) { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); continue; }
                try
                {
                    var repository = scope.ServiceProvider.GetRequiredService<ISharedBookingRepository>();
                    var renderer = scope.ServiceProvider.GetRequiredService<IRecapRenderer>();
                    var recap = await repository.GetRecapAsync(job.RecapId, stoppingToken) ?? throw new InvalidOperationException("Recap record not found.");
                    var route = await repository.GetRouteAsync(recap.SelectedRouteId, true, stoppingToken) ?? throw new InvalidOperationException("Selected route not found.");
                    var media = (await repository.ListMediaAsync(recap.BookingId, stoppingToken))
                        .Where(x => x.InclusionStatus == "Included" && x.MediaVersion <= recap.SourceMediaVersion).ToList();
                    var result = await renderer.RenderAsync(recap, route, media, p => jobs.SetProgressAsync(job, p, stoppingToken), stoppingToken);
                    var warning = result.SkippedPhotos == 0 ? null : $"{result.SkippedPhotos} onleesbare of ontbrekende foto's of videogrepe is veilig oorgeslaan.";
                    await jobs.CompleteAsync(job, result.StorageReference, result.Size, result.DurationSeconds, warning, stoppingToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    _logger.LogError(exception, "Recap job {JobId} failed", job.JobId);
                    await jobs.FailAsync(job, exception.Message.Length > 900 ? exception.Message[..900] : exception.Message, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Recap worker loop failed");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
    }
}
