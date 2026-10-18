using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using MobileApi.Models;
using MobileApi.Options;
using MobileApi.Services;

namespace MobileAPI.Tests;

/// <summary>Runs only where ffmpeg/ffprobe are on PATH (the MobileAPI Docker image has them); reported as skipped elsewhere.</summary>
public sealed class FfmpegFactAttribute : FactAttribute
{
    public FfmpegFactAttribute()
    {
        if (!RecapFfmpegIntegrationTests.ToolsAvailable)
            Skip = "ffmpeg/ffprobe not on PATH - run inside the MobileAPI image (see scripts/run_recap_ffmpeg_tests.ps1).";
    }
}

/// <summary>
/// End-to-end checks of the real media pipeline with real ffmpeg: clip validation on
/// upload (ffprobe) and the sectioned recap renderer that splices VG clips between
/// photos. Inputs are synthesised with ffmpeg itself, including the awkward cases
/// phones produce (portrait, QuickTime, 10-bit HEVC, clips with audio).
/// </summary>
public sealed class RecapFfmpegIntegrationTests : IDisposable
{
    private const int BookingId = 501;
    private const int VgUserId = 77;
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"mikpunt-ffmpeg-it-{Guid.NewGuid():N}");
    private readonly string _inputs;
    private readonly IOptions<SharedBookingOptions> _options;
    private readonly SharedBookingStorage _storage;

    public static bool ToolsAvailable { get; } = CanRun("ffmpeg") && CanRun("ffprobe");

    public RecapFfmpegIntegrationTests()
    {
        _inputs = Path.Combine(_root, "inputs");
        Directory.CreateDirectory(_inputs);
        _options = Options.Create(new SharedBookingOptions
        {
            StoragePath = Path.Combine(_root, "storage"),
            MinimumTemporaryFreeBytes = 0,
            MaxVideoClipSeconds = 30
        });
        _storage = new SharedBookingStorage(new TestEnvironment(_root), _options, new FfprobeVideoProbe(_options));
    }

    [FfmpegFact]
    public async Task Upload_AcceptsRealPhoneStyleClips_AndRecordsTheirDuration()
    {
        var landscape = await MakeClip("landscape.mp4", 1920, 1080, 4, "-c:v libx264 -pix_fmt yuv420p", withAudio: true);
        var portraitMov = await MakeClip("portrait.mov", 720, 1280, 3, "-c:v libx264 -pix_fmt yuv420p -f mov", withAudio: false);
        var hevc10 = await MakeClip("iphone_hdr.mov", 1280, 720, 2, "-c:v libx265 -pix_fmt yuv420p10le -tag:v hvc1 -f mov", withAudio: true);

        var a = await _storage.SaveVideoAsync(BookingId, VgUserId, Upload(landscape, "video/mp4"), CancellationToken.None);
        var b = await _storage.SaveVideoAsync(BookingId, VgUserId, Upload(portraitMov, "video/quicktime"), CancellationToken.None);
        var c = await _storage.SaveVideoAsync(BookingId, VgUserId, Upload(hevc10, "video/quicktime"), CancellationToken.None);

        Assert.Equal(("video/mp4", 1920, 1080), (a.ContentType, a.Width, a.Height));
        Assert.InRange(a.DurationSeconds!.Value, 3.9m, 4.1m);
        Assert.Equal(("video/quicktime", 720, 1280), (b.ContentType, b.Width, b.Height));
        Assert.InRange(b.DurationSeconds!.Value, 2.9m, 3.1m);
        Assert.Equal("video/quicktime", c.ContentType);
        Assert.InRange(c.DurationSeconds!.Value, 1.9m, 2.1m);
    }

    [FfmpegFact]
    public async Task Upload_RejectsTooLongClips_AudioOnlyFiles_AndCorruptVideo()
    {
        var tooLong = await MakeClip("long.mp4", 320, 240, 35, "-c:v libx264 -pix_fmt yuv420p -preset ultrafast", withAudio: false);
        var audioOnly = Path.Combine(_inputs, "voice.m4a");
        await Ffmpeg($"-f lavfi -i sine=frequency=440:duration=3 -c:a aac \"{audioOnly}\"");
        var corrupt = Path.Combine(_inputs, "corrupt.mp4");
        var header = new byte[] { 0, 0, 0, 24, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'i', (byte)'s', (byte)'o', (byte)'m' };
        await File.WriteAllBytesAsync(corrupt, header.Concat(Enumerable.Repeat((byte)0x5a, 50_000)).ToArray());

        var longError = await Assert.ThrowsAsync<InvalidDataException>(() =>
            _storage.SaveVideoAsync(BookingId, VgUserId, Upload(tooLong, "video/mp4"), CancellationToken.None));
        Assert.Contains("30 sekondes", longError.Message);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            _storage.SaveVideoAsync(BookingId, VgUserId, Upload(audioOnly, "video/mp4"), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            _storage.SaveVideoAsync(BookingId, VgUserId, Upload(corrupt, "video/mp4"), CancellationToken.None));

        // Nothing rejected may linger in private storage.
        var stored = Directory.Exists(_options.Value.StoragePath)
            ? Directory.GetFiles(_options.Value.StoragePath, "*", SearchOption.AllDirectories)
            : [];
        Assert.Empty(stored);
    }

    [FfmpegFact]
    public async Task Recap_SplicesClipsBetweenPhotosByCaptureTime_WithoutAudio()
    {
        var start = new DateTime(2026, 10, 3, 6, 0, 0, DateTimeKind.Utc);
        var photo1 = await MakePhoto("dawn.jpg", "red");
        var photo2 = await MakePhoto("kudu.jpg", "green");
        var clipLandscape = await MakeClip("stalk.mp4", 1920, 1080, 4, "-c:v libx264 -pix_fmt yuv420p", withAudio: true);
        var clipPortrait = await MakeClip("shot.mov", 720, 1280, 3, "-c:v libx265 -pix_fmt yuv420p10le -tag:v hvc1 -f mov", withAudio: true);

        // Upload order deliberately differs from capture order.
        var media = new List<BookingMediaDto>
        {
            await StorePhoto(photo2, 1, "PH", start.AddHours(3)),
            await StoreClip(clipPortrait, 2, start.AddHours(2.5)),
            await StorePhoto(photo1, 3, "Hunter", start.AddMinutes(30)),
            await StoreClip(clipLandscape, 4, start.AddHours(1)),
        }.OrderBy(m => m.OriginalCaptureTimestamp).ToList();   // the worker's ListMediaAsync orders by capture time

        var renderer = new FfmpegRecapRenderer(_storage, _options);
        var progress = new List<int>();
        var result = await renderer.RenderAsync(Recap(), Route(start), media, p => { progress.Add(p); return Task.CompletedTask; }, CancellationToken.None);

        Assert.Equal(0, result.SkippedPhotos);
        Assert.True(_storage.TryGetRecap(result.StorageReference, BookingId, out var output));
        var probe = await ProbeJson(output!.FullPath);
        var streams = probe.GetProperty("streams").EnumerateArray().ToList();
        Assert.Single(streams);                                         // no audio track survived
        Assert.Equal("video", streams[0].GetProperty("codec_type").GetString());
        Assert.Equal("h264", streams[0].GetProperty("codec_name").GetString());
        Assert.Equal(1280, streams[0].GetProperty("width").GetInt32());
        Assert.Equal(720, streams[0].GetProperty("height").GetInt32());

        // intro 2 + route 20*0.1 + 2 photos*(0.8+2) + 2 clips*0.8 + 4 + 3 + summary 3 = 21.2 s
        var duration = double.Parse(probe.GetProperty("format").GetProperty("duration").GetString()!, CultureInfo.InvariantCulture);
        Assert.InRange(duration, 20.7, 21.8);
        Assert.Equal(22, result.DurationSeconds);

        // The joined file must decode cleanly end to end (catches broken stream-copy joins).
        var decode = await Run("ffmpeg", $"-v error -i \"{output.FullPath}\" -f null -");
        Assert.Equal(0, decode.ExitCode);
        Assert.True(string.IsNullOrWhiteSpace(decode.Error), decode.Error);

        // Clips play at their capture time: sample frames inside each clip's window and
        // check the test-pattern clip is there (bright, colourful) rather than a dark title card.
        // Timeline: intro 0-2, route 2-4, dawn card 4-4.8, dawn photo 4.8-6.8,
        // stalk card 6.8-7.6, stalk clip 7.6-11.6, kudu card+photo, shot card, shot clip, summary.
        var titleCard = await MeanLuma(output.FullPath, 7.2);
        var clipFrame = await MeanLuma(output.FullPath, 9.6);
        Assert.True(titleCard < 60, $"expected the dark 'VG VIDEO' card at 7.2 s, luma {titleCard}");
        Assert.True(clipFrame > titleCard + 30, $"expected the bright test-pattern clip at 9.6 s, luma {clipFrame} vs card {titleCard}");
        Assert.Contains(20, progress);
        Assert.Contains(95, progress);
    }

    [FfmpegFact]
    public async Task Recap_SkipsAnUnreadableClipButStillRenders()
    {
        var start = new DateTime(2026, 10, 3, 6, 0, 0, DateTimeKind.Utc);
        var photo = await MakePhoto("only.jpg", "blue");
        var clip = await MakeClip("ok.mp4", 640, 360, 2, "-c:v libx264 -pix_fmt yuv420p", withAudio: false);
        var good = await StoreClip(clip, 2, start.AddHours(1));
        // Simulate a stored clip that went bad on disk after upload.
        var broken = await StoreClip(clip, 3, start.AddHours(2));
        Assert.True(_storage.TryGetPhoto(broken.PrivateStorageReference, BookingId, out var brokenFile));
        await File.WriteAllBytesAsync(brokenFile!.FullPath, new byte[4096]);

        var media = new List<BookingMediaDto> { await StorePhoto(photo, 1, "Hunter", start), good, broken };
        var result = await new FfmpegRecapRenderer(_storage, _options)
            .RenderAsync(Recap(), Route(start), media, _ => Task.CompletedTask, CancellationToken.None);

        Assert.Equal(1, result.SkippedPhotos);
        Assert.True(_storage.TryGetRecap(result.StorageReference, BookingId, out var output));
        var decode = await Run("ffmpeg", $"-v error -i \"{output!.FullPath}\" -f null -");
        Assert.Equal(0, decode.ExitCode);
    }

    // ---- helpers -------------------------------------------------------------------

    private static BookingRecapDto Recap() => new()
    {
        RecapId = 9001,
        BookingId = BookingId,
        Title = "Kudu hunt Limpopo",
        BookingStartDate = new DateTime(2026, 10, 2),
        BookingEndDate = new DateTime(2026, 10, 4)
    };

    private static BookingRouteDto Route(DateTime start) => new()
    {
        BookingId = BookingId,
        DistanceMeters = 5400,
        Points = Enumerable.Range(0, 20).Select(i => new RoutePointRequest
        {
            Latitude = -24.0 + i * 0.001,
            Longitude = 29.0 + Math.Sin(i / 3.0) * 0.002,
            Timestamp = start.AddMinutes(i * 10)
        }).ToList(),
        Pins = [new RoutePinRequest { Id = "shot-1", Type = "Shot", Latitude = -23.99, Longitude = 29.0, Timestamp = start.AddHours(2.5) }]
    };

    private async Task<BookingMediaDto> StorePhoto(string path, long id, string role, DateTime capturedAt)
    {
        var stored = await _storage.SavePhotoAsync(BookingId, VgUserId, Upload(path, "image/jpeg"), CancellationToken.None);
        return Dto(id, stored, role, "Photo", capturedAt);
    }

    private async Task<BookingMediaDto> StoreClip(string path, long id, DateTime capturedAt)
    {
        var stored = await _storage.SaveVideoAsync(BookingId, VgUserId, Upload(path, "video/mp4"), CancellationToken.None);
        return Dto(id, stored, "VG", "Video", capturedAt);
    }

    private static BookingMediaDto Dto(long id, StoredBookingMedia stored, string role, string type, DateTime capturedAt) => new()
    {
        MediaId = id,
        BookingId = BookingId,
        UploaderUserId = VgUserId,
        UploaderRole = role,
        PrivateStorageReference = stored.StorageReference,
        ContentType = stored.ContentType,
        FileSizeBytes = stored.Length,
        Width = stored.Width,
        Height = stored.Height,
        SourceType = type,
        DurationSeconds = stored.DurationSeconds,
        OriginalCaptureTimestamp = capturedAt,
        Caption = type == "Video" ? "VG clip" : "Photo",
        MatchMethod = "Timestamp",
        MatchConfidence = "High"
    };

    private async Task<string> MakeClip(string name, int width, int height, int seconds, string codecArgs, bool withAudio)
    {
        var path = Path.Combine(_inputs, name);
        var audio = withAudio ? $"-f lavfi -i sine=frequency=880:duration={seconds} -c:a aac -shortest " : string.Empty;
        await Ffmpeg($"-f lavfi -i testsrc2=size={width}x{height}:rate=30:duration={seconds} {audio}{codecArgs} \"{path}\"");
        return path;
    }

    private async Task<string> MakePhoto(string name, string colour)
    {
        var path = Path.Combine(_inputs, name);
        await Ffmpeg($"-f lavfi -i color=c={colour}:size=1600x1200 -frames:v 1 \"{path}\"");
        return path;
    }

    private static async Task Ffmpeg(string arguments)
    {
        var result = await Run("ffmpeg", "-hide_banner -loglevel error -y " + arguments);
        Assert.True(result.ExitCode == 0, $"ffmpeg failed: {result.Error}");
    }

    private static async Task<JsonElement> ProbeJson(string path)
    {
        var result = await Run("ffprobe", $"-v error -show_entries stream=codec_type,codec_name,width,height:format=duration -of json \"{path}\"");
        Assert.Equal(0, result.ExitCode);
        return JsonDocument.Parse(result.Output).RootElement.Clone();
    }

    /// <summary>Average brightness (0-255) of the frame at a timestamp, via ffmpeg's signalstats.</summary>
    private static async Task<double> MeanLuma(string path, double seconds)
    {
        var at = seconds.ToString("0.00", CultureInfo.InvariantCulture);
        var result = await Run("ffmpeg", $"-v error -ss {at} -i \"{path}\" -frames:v 1 -vf signalstats,metadata=print:key=lavfi.signalstats.YAVG:file=- -f null -");
        Assert.Equal(0, result.ExitCode);
        var line = result.Output.Split('\n').First(l => l.Contains("YAVG"));
        return double.Parse(line[(line.IndexOf('=') + 1)..].Trim(), CultureInfo.InvariantCulture);
    }

    private static FormFile Upload(string path, string contentType)
    {
        var bytes = File.ReadAllBytes(path);
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", Path.GetFileName(path))
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }

    private static async Task<(int ExitCode, string Output, string Error)> Run(string exe, string arguments)
    {
        var start = new ProcessStartInfo(exe, arguments) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await output, await error);
    }

    private static bool CanRun(string exe)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(exe, "-version") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false });
            process!.WaitForExit(10_000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    private sealed class TestEnvironment(string root) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "MobileAPI.Tests";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = root;
        public string EnvironmentName { get; set; } = "Test";
        public string WebRootPath { get; set; } = root;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
