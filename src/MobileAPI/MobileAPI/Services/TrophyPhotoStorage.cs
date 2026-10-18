namespace MobileApi.Services;

public sealed record TrophyPhotoUploadResult(
    string RelativePath,
    string ContentType,
    long Length
);

public sealed record TrophyPhotoFile(
    string FullPath,
    string ContentType
);

public interface ITrophyPhotoStorage
{
    Task<TrophyPhotoUploadResult> SaveAsync(
        IFormFile photo,
        int ownerUserId,
        CancellationToken cancellationToken = default
    );

    bool IsOwnedPath(string? photoPath, int ownerUserId);

    bool TryGetOwnedFile(
        string? photoPath,
        int ownerUserId,
        out TrophyPhotoFile? photoFile
    );

    Task<bool> DeleteIfOwnedAsync(
        string? photoPath,
        int ownerUserId,
        CancellationToken cancellationToken = default
    );
}

/// <summary>
/// Stores validated trophy photos outside the database. File extensions and
/// content types are derived from magic bytes, never from the supplied name.
/// </summary>
public sealed class TrophyPhotoStorage : ITrophyPhotoStorage
{
    public const long MaxFileBytes = 10 * 1024 * 1024;
    public const string RequestPath = "/uploads/trophies";

    private readonly string _rootPath;

    public TrophyPhotoStorage(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new ArgumentException("A trophy photo storage path is required.", nameof(rootPath));
        }

        _rootPath = Path.GetFullPath(rootPath);
        Directory.CreateDirectory(_rootPath);
    }

    public async Task<TrophyPhotoUploadResult> SaveAsync(
        IFormFile photo,
        int ownerUserId,
        CancellationToken cancellationToken = default)
    {
        if (ownerUserId <= 0)
        {
            throw new InvalidDataException("Die gebruiker kon nie bevestig word nie.");
        }

        if (photo is null || photo.Length <= 0)
        {
            throw new InvalidDataException("Kies asseblief 'n foto om op te laai.");
        }

        if (photo.Length > MaxFileBytes)
        {
            throw new InvalidDataException("Die foto mag nie groter as 10 MB wees nie.");
        }

        await using var input = photo.OpenReadStream();
        var header = new byte[12];
        var headerLength = 0;
        while (headerLength < header.Length)
        {
            var read = await input.ReadAsync(
                header.AsMemory(headerLength, header.Length - headerLength),
                cancellationToken
            );
            if (read == 0)
            {
                break;
            }
            headerLength += read;
        }

        var format = DetectFormat(header.AsSpan(0, headerLength));
        if (format is null)
        {
            throw new InvalidDataException("Slegs JPEG-, PNG- of WebP-foto's word aanvaar.");
        }

        var ownerDirectory = Path.Combine(_rootPath, ownerUserId.ToString());
        Directory.CreateDirectory(ownerDirectory);

        var fileName = $"{Guid.NewGuid():N}{format.Value.Extension}";
        var fullPath = Path.Combine(ownerDirectory, fileName);

        try
        {
            await using var output = new FileStream(
                fullPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true
            );

            await output.WriteAsync(header.AsMemory(0, headerLength), cancellationToken);
            long totalBytes = headerLength;
            var buffer = new byte[81920];

            while (true)
            {
                var read = await input.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                {
                    break;
                }

                totalBytes += read;
                if (totalBytes > MaxFileBytes)
                {
                    throw new InvalidDataException("Die foto mag nie groter as 10 MB wees nie.");
                }

                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            await output.FlushAsync(cancellationToken);

            return new TrophyPhotoUploadResult(
                $"{RequestPath}/{ownerUserId}/{fileName}",
                format.Value.ContentType,
                totalBytes
            );
        }
        catch
        {
            try
            {
                File.Delete(fullPath);
            }
            catch
            {
                // Preserve the original upload error.
            }
            throw;
        }
    }

    public bool IsOwnedPath(string? photoPath, int ownerUserId)
    {
        return TryResolveOwnedPath(photoPath, ownerUserId, out var fullPath) &&
            File.Exists(fullPath);
    }

    public bool TryGetOwnedFile(
        string? photoPath,
        int ownerUserId,
        out TrophyPhotoFile? photoFile)
    {
        photoFile = null;
        if (!TryResolveOwnedPath(photoPath, ownerUserId, out var fullPath) ||
            !File.Exists(fullPath))
        {
            return false;
        }

        var contentType = Path.GetExtension(fullPath).ToLowerInvariant() switch
        {
            ".jpg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => null
        };
        if (contentType is null)
        {
            return false;
        }

        photoFile = new TrophyPhotoFile(fullPath, contentType);
        return true;
    }

    public Task<bool> DeleteIfOwnedAsync(
        string? photoPath,
        int ownerUserId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryResolveOwnedPath(photoPath, ownerUserId, out var fullPath))
        {
            return Task.FromResult(false);
        }

        try
        {
            if (!File.Exists(fullPath))
            {
                return Task.FromResult(false);
            }

            File.Delete(fullPath);
            return Task.FromResult(true);
        }
        catch (IOException)
        {
            return Task.FromResult(false);
        }
        catch (UnauthorizedAccessException)
        {
            return Task.FromResult(false);
        }
    }

    private bool TryResolveOwnedPath(
        string? photoPath,
        int ownerUserId,
        out string fullPath)
    {
        fullPath = string.Empty;
        if (ownerUserId <= 0 || string.IsNullOrWhiteSpace(photoPath))
        {
            return false;
        }

        // Root-relative request paths are the public contract returned by SaveAsync.
        // The exact owner prefix and confined single-file checks below also reject
        // external URLs, absolute file paths, and traversal attempts cross-platform.
        var value = photoPath.Trim();
        try
        {
            value = Uri.UnescapeDataString(value).Replace('\\', '/');
        }
        catch (UriFormatException)
        {
            return false;
        }
        var prefix = $"{RequestPath}/{ownerUserId}/";
        if (!value.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var fileName = value[prefix.Length..];
        if (string.IsNullOrWhiteSpace(fileName) ||
            fileName.Contains('/') ||
            fileName != Path.GetFileName(fileName))
        {
            return false;
        }

        var ownerDirectory = Path.GetFullPath(Path.Combine(_rootPath, ownerUserId.ToString()));
        var candidate = Path.GetFullPath(Path.Combine(ownerDirectory, fileName));
        if (!candidate.StartsWith(
                ownerDirectory + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        fullPath = candidate;
        return true;
    }

    private static (string Extension, string ContentType)? DetectFormat(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 3 &&
            bytes[0] == 0xFF &&
            bytes[1] == 0xD8 &&
            bytes[2] == 0xFF)
        {
            return (".jpg", "image/jpeg");
        }

        ReadOnlySpan<byte> pngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (bytes.Length >= pngSignature.Length && bytes[..pngSignature.Length].SequenceEqual(pngSignature))
        {
            return (".png", "image/png");
        }

        if (bytes.Length >= 12 &&
            bytes[..4].SequenceEqual("RIFF"u8) &&
            bytes.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return (".webp", "image/webp");
        }

        return null;
    }
}
