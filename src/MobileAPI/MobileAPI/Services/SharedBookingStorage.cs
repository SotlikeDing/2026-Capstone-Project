using System.Buffers.Binary;
using Microsoft.Extensions.Options;

namespace MobileApi.Services;

public sealed record StoredBookingMedia(
    string StorageReference,
    string ContentType,
    long Length,
    int Width,
    int Height,
    decimal? DurationSeconds = null);

public sealed record PrivateStorageFile(string FullPath, string ContentType, long Length);

public interface ISharedBookingStorage
{
    Task<StoredBookingMedia> SavePhotoAsync(int bookingId, int userId, IFormFile photo, CancellationToken cancellationToken);
    Task<StoredBookingMedia> SaveVideoAsync(int bookingId, int userId, IFormFile video, CancellationToken cancellationToken);
    bool TryGetPhoto(string? storageReference, int bookingId, out PrivateStorageFile? file);
    bool TryGetRecap(string? storageReference, int bookingId, out PrivateStorageFile? file);
    string GetRecapOutputPath(int bookingId, long recapId);
    string ToRecapStorageReference(int bookingId, long recapId);
    Task<bool> DeleteAsync(string? storageReference, CancellationToken cancellationToken = default);
    long GetAvailableBytes();
}

/// <summary>Private, path-confined binary storage shared by the media and recap workers.</summary>
public sealed class SharedBookingStorage : ISharedBookingStorage
{
    private readonly string _root;
    private readonly Options.SharedBookingOptions _options;
    private readonly IVideoProbe _videoProbe;

    public SharedBookingStorage(
        IWebHostEnvironment environment,
        IOptions<Options.SharedBookingOptions> options,
        IVideoProbe? videoProbe = null)
    {
        _options = options.Value;
        _videoProbe = videoProbe ?? new FfprobeVideoProbe(options);
        var configured = _options.StoragePath;
        _root = Path.GetFullPath(Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(environment.ContentRootPath, configured));
        Directory.CreateDirectory(_root);
    }

    public async Task<StoredBookingMedia> SavePhotoAsync(
        int bookingId,
        int userId,
        IFormFile photo,
        CancellationToken cancellationToken)
    {
        if (bookingId <= 0 || userId <= 0 || photo.Length <= 0)
            throw new InvalidDataException("Kies asseblief 'n geldige foto.");
        if (photo.Length > _options.MaxPhotoBytes)
            throw new InvalidDataException($"Die foto mag nie groter as {_options.MaxPhotoBytes / 1024 / 1024} MB wees nie.");

        await using var memory = new MemoryStream((int)Math.Min(photo.Length, int.MaxValue));
        await photo.CopyToAsync(memory, cancellationToken);
        if (memory.Length != photo.Length || memory.Length == 0 || memory.Length > _options.MaxPhotoBytes)
            throw new InvalidDataException("Die foto-inhoud is onvolledig of te groot.");

        var bytes = memory.ToArray();
        var format = ImageInspector.Inspect(bytes)
            ?? throw new InvalidDataException("Slegs geldige JPEG-, PNG- of WebP-foto's word aanvaar.");
        if (format.Width <= 0 || format.Height <= 0 || format.Width > 50_000 || format.Height > 50_000)
            throw new InvalidDataException("Die foto se afmetings is ongeldig.");

        var reference = $"media/{bookingId}/{userId}/{Guid.NewGuid():N}{format.Extension}";
        var path = ResolveNew(reference);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            await File.WriteAllBytesAsync(path, bytes, cancellationToken);
        }
        catch
        {
            TryDelete(path);
            throw;
        }
        return new StoredBookingMedia(reference, format.ContentType, bytes.LongLength, format.Width, format.Height);
    }

    /// <summary>
    /// Streams a short clip to private storage, then lets ffprobe confirm it really is
    /// a decodable video within the clip limits. Anything that fails is deleted before
    /// a database row can point at it.
    /// </summary>
    public async Task<StoredBookingMedia> SaveVideoAsync(
        int bookingId,
        int userId,
        IFormFile video,
        CancellationToken cancellationToken)
    {
        if (bookingId <= 0 || userId <= 0 || video.Length <= 0)
            throw new InvalidDataException("Kies asseblief 'n geldige videogreep.");
        if (video.Length > _options.MaxVideoClipBytes)
            throw new InvalidDataException($"Die videogreep mag nie groter as {_options.MaxVideoClipBytes / 1024 / 1024} MB wees nie.");

        await using var input = video.OpenReadStream();
        var header = new byte[12];
        var headerLength = await input.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken);
        var format = VideoContainer.Inspect(header.AsSpan(0, headerLength))
            ?? throw new InvalidDataException("Slegs MP4- of MOV-videogrepe word aanvaar.");

        var reference = $"media/{bookingId}/{userId}/{Guid.NewGuid():N}{format.Extension}";
        var path = ResolveNew(reference);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        long written = headerLength;
        try
        {
            await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await output.WriteAsync(header.AsMemory(0, headerLength), cancellationToken);
                var buffer = new byte[81920];
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    written += read;
                    if (written > _options.MaxVideoClipBytes)
                        throw new InvalidDataException("Die videogreep is te groot.");
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
            }
            if (written != video.Length)
                throw new InvalidDataException("Die video-inhoud is onvolledig.");

            var probe = await _videoProbe.ProbeAsync(path, cancellationToken)
                ?? throw new InvalidDataException("Die videogreep kon nie gelees word nie.");
            if (probe.Width <= 0 || probe.Height <= 0 || probe.Width > 8192 || probe.Height > 8192)
                throw new InvalidDataException("Die video se afmetings is ongeldig.");
            if (probe.DurationSeconds <= 0)
                throw new InvalidDataException("Die videogreep het geen speelbare lengte nie.");
            // Small tolerance: phone recorders often overshoot the requested length by a few frames.
            if (probe.DurationSeconds > _options.MaxVideoClipSeconds + 0.5m)
                throw new InvalidDataException($"Videogrepe mag hoogstens {_options.MaxVideoClipSeconds} sekondes lank wees.");

            return new StoredBookingMedia(reference, format.ContentType, written, probe.Width, probe.Height,
                Math.Round(probe.DurationSeconds, 3));
        }
        catch
        {
            TryDelete(path);
            throw;
        }
    }

    public bool TryGetPhoto(string? storageReference, int bookingId, out PrivateStorageFile? file) =>
        TryGet(storageReference, $"media/{bookingId}/", out file);

    public bool TryGetRecap(string? storageReference, int bookingId, out PrivateStorageFile? file) =>
        TryGet(storageReference, $"recaps/{bookingId}/", out file);

    public string GetRecapOutputPath(int bookingId, long recapId)
    {
        var reference = ToRecapStorageReference(bookingId, recapId);
        var path = ResolveNew(reference);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }

    public string ToRecapStorageReference(int bookingId, long recapId) => $"recaps/{bookingId}/{recapId}.mp4";

    public Task<bool> DeleteAsync(string? storageReference, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryResolve(storageReference, out var path)) return Task.FromResult(false);
        return Task.FromResult(TryDelete(path));
    }

    public long GetAvailableBytes()
    {
        var root = Path.GetPathRoot(_root);
        return string.IsNullOrWhiteSpace(root) ? 0 : new DriveInfo(root).AvailableFreeSpace;
    }

    private bool TryGet(string? reference, string expectedPrefix, out PrivateStorageFile? file)
    {
        file = null;
        var normalized = (reference ?? string.Empty).Replace('\\', '/');
        if (!normalized.StartsWith(expectedPrefix, StringComparison.Ordinal) || !TryResolve(normalized, out var path) || !File.Exists(path))
            return false;
        var contentType = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".mp4" => "video/mp4",
            ".mov" => "video/quicktime",
            _ => null
        };
        if (contentType is null) return false;
        file = new PrivateStorageFile(path, contentType, new FileInfo(path).Length);
        return true;
    }

    private string ResolveNew(string reference)
    {
        if (!TryResolve(reference, out var path)) throw new InvalidOperationException("Unsafe private storage reference.");
        return path;
    }

    private bool TryResolve(string? reference, out string path)
    {
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(reference) || Uri.TryCreate(reference, UriKind.Absolute, out _)) return false;
        var normalized = reference.Replace('\\', '/').TrimStart('/');
        if (normalized.Contains("../", StringComparison.Ordinal) || normalized.Contains("..\\", StringComparison.Ordinal)) return false;
        var candidate = Path.GetFullPath(Path.Combine(_root, normalized.Replace('/', Path.DirectorySeparatorChar)));
        if (!candidate.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return false;
        path = candidate;
        return true;
    }

    private static bool TryDelete(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            File.Delete(path);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private sealed record ImageFormat(string Extension, string ContentType, int Width, int Height);

    private sealed record VideoFormat(string Extension, string ContentType);

    /// <summary>ISO base media (MP4/MOV) files open with a 'ftyp' box; the major brand separates QuickTime from MP4.</summary>
    private static class VideoContainer
    {
        public static VideoFormat? Inspect(ReadOnlySpan<byte> data)
        {
            if (data.Length < 12 || !data.Slice(4, 4).SequenceEqual("ftyp"u8)) return null;
            var boxSize = BinaryPrimitives.ReadUInt32BigEndian(data[..4]);
            if (boxSize is < 12 or > 4096) return null;
            return data.Slice(8, 4).SequenceEqual("qt  "u8)
                ? new VideoFormat(".mov", "video/quicktime")
                : new VideoFormat(".mp4", "video/mp4");
        }
    }

    private static class ImageInspector
    {
        public static ImageFormat? Inspect(ReadOnlySpan<byte> data)
        {
            if (data.Length >= 24 && data[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }))
            {
                if (data.Length < 45 || BinaryPrimitives.ReadUInt32BigEndian(data.Slice(8, 4)) != 13 ||
                    !data.Slice(12, 4).SequenceEqual("IHDR"u8) || !HasCompletePngChunks(data)) return null;
                return new ImageFormat(".png", "image/png",
                    BinaryPrimitives.ReadInt32BigEndian(data.Slice(16, 4)),
                    BinaryPrimitives.ReadInt32BigEndian(data.Slice(20, 4)));
            }
            if (data.Length >= 30 && data[0] == 0xff && data[1] == 0xd8 &&
                data[^2] == 0xff && data[^1] == 0xd9)
                return InspectJpeg(data);
            if (data.Length >= 30 && data[..4].SequenceEqual("RIFF"u8) &&
                data.Slice(8, 4).SequenceEqual("WEBP"u8) &&
                BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(4, 4)) + 8 == data.Length)
                return InspectWebp(data);
            return null;
        }

        private static bool HasCompletePngChunks(ReadOnlySpan<byte> data)
        {
            var offset = 8;
            while (offset + 12 <= data.Length)
            {
                var length = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset, 4));
                if (length > int.MaxValue || offset + 12L + length > data.Length) return false;
                var type = data.Slice(offset + 4, 4);
                offset += 12 + (int)length;
                if (type.SequenceEqual("IEND"u8)) return length == 0 && offset == data.Length;
            }
            return false;
        }

        private static ImageFormat? InspectJpeg(ReadOnlySpan<byte> data)
        {
            var offset = 2;
            while (offset + 4 <= data.Length)
            {
                if (data[offset] != 0xff) return null;
                var marker = data[offset + 1];
                offset += 2;
                if (marker is 0xd8 or 0xd9) continue;
                if (offset + 2 > data.Length) return null;
                var length = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset, 2));
                if (length < 2 || offset + length > data.Length) return null;
                if (marker is >= 0xc0 and <= 0xc3 or >= 0xc5 and <= 0xc7 or >= 0xc9 and <= 0xcb or >= 0xcd and <= 0xcf)
                {
                    if (length < 7) return null;
                    var height = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset + 3, 2));
                    var width = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset + 5, 2));
                    return new ImageFormat(".jpg", "image/jpeg", width, height);
                }
                offset += length;
            }
            return null;
        }

        private static ImageFormat? InspectWebp(ReadOnlySpan<byte> data)
        {
            var kind = data.Slice(12, 4);
            if (kind.SequenceEqual("VP8X"u8) && data.Length >= 30)
            {
                var width = 1 + data[24] + (data[25] << 8) + (data[26] << 16);
                var height = 1 + data[27] + (data[28] << 8) + (data[29] << 16);
                return new ImageFormat(".webp", "image/webp", width, height);
            }
            if (kind.SequenceEqual("VP8L"u8) && data.Length >= 25 && data[20] == 0x2f)
            {
                var bits = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(21, 4));
                return new ImageFormat(".webp", "image/webp", (int)((bits & 0x3fff) + 1), (int)(((bits >> 14) & 0x3fff) + 1));
            }
            if (kind.SequenceEqual("VP8 "u8) && data.Length >= 30 && data[23] == 0x9d && data[24] == 0x01 && data[25] == 0x2a)
            {
                return new ImageFormat(".webp", "image/webp",
                    BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(26, 2)) & 0x3fff,
                    BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(28, 2)) & 0x3fff);
            }
            return null;
        }
    }
}
