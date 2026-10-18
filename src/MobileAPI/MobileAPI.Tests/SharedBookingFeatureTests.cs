using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using MobileApi.Controllers;
using MobileApi.Models;
using MobileApi.Options;
using MobileApi.Services;
using System.Net;

namespace MobileAPI.Tests;

public sealed class SharedBookingFeatureTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"mikpunt-shared-booking-{Guid.NewGuid():N}");

    [Theory]
    [InlineData("Hunter", MobileRole.Hunter)]
    [InlineData("jagter", MobileRole.Hunter)]
    [InlineData("professional-hunter", MobileRole.Ph)]
    [InlineData("Videograaf", MobileRole.Vg)]
    [InlineData("ProjectManager", MobileRole.Restricted)]
    [InlineData("Farmer", MobileRole.Restricted)]
    [InlineData("unknown", MobileRole.Restricted)]
    [InlineData(null, MobileRole.Restricted)]
    public void ParseRole_OnlyAllowsSupportedMobileRoles(string? roleCode, MobileRole expected)
    {
        Assert.Equal(expected, MobileAccessService.ParseRole(roleCode));
    }

    [Fact]
    public void Capabilities_AreLeastPrivilegePerRole()
    {
        var hunter = MobileAccessService.CapabilitiesFor(MobileRole.Hunter);
        var ph = MobileAccessService.CapabilitiesFor(MobileRole.Ph);
        var vg = MobileAccessService.CapabilitiesFor(MobileRole.Vg);
        var restricted = MobileAccessService.CapabilitiesFor(MobileRole.Restricted);

        Assert.Contains(MobileCapabilityNames.SubmitTrophy, hunter);
        Assert.DoesNotContain(MobileCapabilityNames.ReviewTrophies, hunter);
        Assert.DoesNotContain(MobileCapabilityNames.PublishRecap, hunter);
        Assert.Contains(MobileCapabilityNames.ReviewTrophies, ph);
        Assert.Contains(MobileCapabilityNames.SelectRoute, ph);
        Assert.Contains(MobileCapabilityNames.PublishRecap, ph);
        Assert.DoesNotContain(MobileCapabilityNames.SubmitTrophy, ph);
        Assert.Contains(MobileCapabilityNames.UploadBookingMedia, vg);
        Assert.DoesNotContain(MobileCapabilityNames.PublishRecap, vg);
        Assert.Contains(MobileCapabilityNames.UploadBookingVideo, vg);
        Assert.DoesNotContain(MobileCapabilityNames.UploadBookingVideo, hunter);
        Assert.DoesNotContain(MobileCapabilityNames.UploadBookingVideo, ph);
        Assert.Empty(restricted);
    }

    [Fact]
    public async Task VideoStorage_StoresProbedMp4ClipWithDuration()
    {
        var storage = CreateStorage(new FakeVideoProbe(new VideoProbeResult(1920, 1080, 12.3456m)));

        var saved = await storage.SaveVideoAsync(17, 42, FormFile(Mp4Header("isom"), "clip.mp4", "video/mp4"), CancellationToken.None);

        Assert.Equal("video/mp4", saved.ContentType);
        Assert.EndsWith(".mp4", saved.StorageReference);
        Assert.Equal(1920, saved.Width);
        Assert.Equal(12.346m, saved.DurationSeconds);
        Assert.True(storage.TryGetPhoto(saved.StorageReference, 17, out var file));
        Assert.Equal("video/mp4", file!.ContentType);
    }

    [Fact]
    public async Task VideoStorage_DetectsQuickTimeFromContentNotName()
    {
        var storage = CreateStorage(new FakeVideoProbe(new VideoProbeResult(1080, 1920, 5m)));

        var saved = await storage.SaveVideoAsync(17, 42, FormFile(Mp4Header("qt  "), "clip.mp4", "video/mp4"), CancellationToken.None);

        Assert.Equal("video/quicktime", saved.ContentType);
        Assert.EndsWith(".mov", saved.StorageReference);
    }

    [Fact]
    public async Task VideoStorage_RejectsNonVideoContentWithoutProbing()
    {
        var probe = new FakeVideoProbe(new VideoProbeResult(1920, 1080, 5m));
        var storage = CreateStorage(probe);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            storage.SaveVideoAsync(17, 42, FormFile("not a video at all"u8.ToArray(), "clip.mp4", "video/mp4"), CancellationToken.None));
        Assert.Equal(0, probe.Calls);
    }

    [Theory]
    [InlineData(31.0)]   // longer than the 30 s clip limit
    [InlineData(0.0)]    // no playable duration
    public async Task VideoStorage_RejectsOutOfRangeDurationsAndCleansUp(double seconds)
    {
        var storage = CreateStorage(new FakeVideoProbe(new VideoProbeResult(1920, 1080, (decimal)seconds)));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            storage.SaveVideoAsync(17, 42, FormFile(Mp4Header("isom"), "clip.mp4", "video/mp4"), CancellationToken.None));
        Assert.Empty(Directory.Exists(_root) ? Directory.GetFiles(_root, "*.mp4", SearchOption.AllDirectories) : []);
    }

    [Fact]
    public async Task VideoStorage_RejectsUndecodableClipAndCleansUp()
    {
        var storage = CreateStorage(new FakeVideoProbe(null));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            storage.SaveVideoAsync(17, 42, FormFile(Mp4Header("isom"), "clip.mp4", "video/mp4"), CancellationToken.None));
        Assert.Empty(Directory.Exists(_root) ? Directory.GetFiles(_root, "*.*", SearchOption.AllDirectories) : []);
    }

    [Fact]
    public void FfprobeOutput_PrefersContainerDurationAndRejectsAudioOnly()
    {
        var parsed = FfprobeVideoProbe.Parse("""
            {"streams":[{"width":1280,"height":720,"duration":"9.900000"}],"format":{"duration":"10.033000"}}
            """);
        Assert.Equal(new VideoProbeResult(1280, 720, 10.033m), parsed);

        var streamOnly = FfprobeVideoProbe.Parse("""{"streams":[{"width":640,"height":360,"duration":"4.5"}],"format":{}}""");
        Assert.Equal(4.5m, streamOnly!.DurationSeconds);

        Assert.Null(FfprobeVideoProbe.Parse("""{"streams":[],"format":{"duration":"3.0"}}"""));
        Assert.Null(FfprobeVideoProbe.Parse("not json"));
    }

    [Theory]
    [InlineData("Die skoot!", "DIE SKOOT!")]
    [InlineData("Eerste skoot ná ’n lang dag", "EERSTE SKOOT NA 'N LANG DAG")]
    [InlineData("Koedoe-bul, 52\" / ê ë ô û", "KOEDOE-BUL, 52\" / E E O U")]
    public void RecapCards_ReduceAfrikaansAccentsInsteadOfQuestionMarks(string caption, string expected)
    {
        Assert.Equal(expected, FfmpegRecapRenderer.ToFontText(caption));
    }

    private static byte[] Mp4Header(string brand)
    {
        var bytes = new byte[64];
        new byte[] { 0, 0, 0, 24 }.CopyTo(bytes, 0);
        "ftyp"u8.CopyTo(bytes.AsSpan(4));
        System.Text.Encoding.ASCII.GetBytes(brand).CopyTo(bytes, 8);
        return bytes;
    }

    private sealed class FakeVideoProbe(VideoProbeResult? result) : IVideoProbe
    {
        public int Calls { get; private set; }

        public Task<VideoProbeResult?> ProbeAsync(string path, CancellationToken cancellationToken)
        {
            Calls++;
            Assert.True(File.Exists(path));
            return Task.FromResult(result);
        }
    }

    [Fact]
    public async Task Storage_ValidatesContentAndConfinesBookingReads()
    {
        var storage = CreateStorage();
        var png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAIAAAADCAIAAAA2iEnWAAAADElEQVR4nGNgIA0AAAAWAAGxAwKSAAAAAElFTkSuQmCC");

        var saved = await storage.SavePhotoAsync(
            17,
            42,
            FormFile(png, "misleading.txt", "text/plain"),
            CancellationToken.None);

        Assert.Equal("image/png", saved.ContentType);
        Assert.Equal(2, saved.Width);
        Assert.Equal(3, saved.Height);
        Assert.True(storage.TryGetPhoto(saved.StorageReference, 17, out var file));
        Assert.NotNull(file);
        Assert.False(storage.TryGetPhoto(saved.StorageReference, 18, out _));
        Assert.False(storage.TryGetPhoto("../outside.png", 17, out _));
        Assert.False(storage.TryGetPhoto("https://evil.example/photo.png", 17, out _));
    }

    [Fact]
    public async Task Storage_RejectsSpoofedImageContent()
    {
        var storage = CreateStorage();
        var photo = FormFile("not an image"u8.ToArray(), "fake.jpg", "image/jpeg");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            storage.SavePhotoAsync(17, 42, photo, CancellationToken.None));
    }

    [Fact]
    public async Task Storage_RejectsTruncatedPngWithPlausibleHeader()
    {
        var storage = CreateStorage();
        var truncated = new byte[24];
        new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }.CopyTo(truncated, 0);
        "IHDR"u8.CopyTo(truncated.AsSpan(12, 4));
        new byte[] { 0, 0, 0, 2, 0, 0, 0, 3 }.CopyTo(truncated, 16);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            storage.SavePhotoAsync(
                17,
                42,
                FormFile(truncated, "truncated.png", "image/png"),
                CancellationToken.None));
    }

    [Fact]
    public void PhotoMatcher_PrefersGpsThenFallsBackToTimestamp()
    {
        var start = new DateTime(2026, 10, 10, 8, 0, 0, DateTimeKind.Utc);
        var route = new BookingRouteDto
        {
            Points =
            [
                new RoutePointRequest { Latitude = -25.75, Longitude = 28.19, Timestamp = start },
                new RoutePointRequest { Latitude = -25.751, Longitude = 28.191, Timestamp = start.AddMinutes(10) }
            ],
            Pins =
            [
                new RoutePinRequest { Id = "shot-1", Type = "Shot", Latitude = -25.75, Longitude = 28.19, Timestamp = start }
            ]
        };

        var gps = BookingPhotoMatcher.Match(route, start.AddHours(3), -25.75m, 28.19m);
        Assert.Equal("GPS", gps.Method);
        Assert.Equal("High", gps.Confidence);
        Assert.Equal("shot-1", gps.PinId);

        var timestamp = BookingPhotoMatcher.Match(route, start.AddMinutes(10), null, null);
        Assert.Equal("Pin", timestamp.Method);
        Assert.Equal("High", timestamp.Confidence);
        Assert.Equal(1, timestamp.RoutePointIndex);

        var unmatched = BookingPhotoMatcher.Match(route, null, null, null);
        Assert.Equal("Unmatched", unmatched.Method);
    }

    [Fact]
    public async Task ResumableStatusQuery_UsesServerOffsetAndDetectsExpiredSession()
    {
        using var resumeClient = new HttpClient(new StubHandler(_ =>
        {
            var response = new HttpResponseMessage((HttpStatusCode)308);
            response.Headers.TryAddWithoutValidation("Range", "bytes=0-1048575");
            return response;
        }));
        var resume = await YoutubePublishWorker.QueryUploadAsync(
            resumeClient,
            new Uri("https://upload.example/session"),
            2_000_000,
            CancellationToken.None);
        Assert.Equal(1_048_576, resume.Offset);
        Assert.Null(resume.VideoId);
        Assert.False(resume.SessionExpired);

        using var expiredClient = new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.Gone)));
        var expired = await YoutubePublishWorker.QueryUploadAsync(
            expiredClient,
            new Uri("https://upload.example/session"),
            2_000_000,
            CancellationToken.None);
        Assert.True(expired.SessionExpired);
    }

    private SharedBookingStorage CreateStorage(IVideoProbe? videoProbe = null)
    {
        var options = Options.Create(new SharedBookingOptions
        {
            StoragePath = _root,
            MaxPhotoBytes = 1024 * 1024,
            MaxVideoClipBytes = 1024 * 1024,
            MaxVideoClipSeconds = 30
        });
        return new SharedBookingStorage(new TestEnvironment(_root), options, videoProbe);
    }

    private static FormFile FormFile(byte[] bytes, string fileName, string contentType) =>
        new(new MemoryStream(bytes), 0, bytes.Length, "photo", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
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

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> response)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(response(request));
    }
}
