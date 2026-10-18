using Microsoft.AspNetCore.Http;
using MobileApi.Services;

namespace MobileAPI.Tests;

public sealed class TrophyPhotoStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"mikpunt-trophy-photos-{Guid.NewGuid():N}"
    );

    [Fact]
    public async Task SaveAsync_DetectsJpegAndScopesPathToOwner()
    {
        var storage = new TrophyPhotoStorage(_root);
        var bytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4 };
        var photo = CreateFormFile(bytes, "not-really-a-text-file.txt", "text/plain");

        var result = await storage.SaveAsync(photo, 42);

        Assert.StartsWith("/uploads/trophies/42/", result.RelativePath);
        Assert.EndsWith(".jpg", result.RelativePath);
        Assert.Equal("image/jpeg", result.ContentType);
        Assert.True(storage.IsOwnedPath(result.RelativePath, 42));
        Assert.False(storage.IsOwnedPath(result.RelativePath, 7));
        Assert.True(storage.TryGetOwnedFile(result.RelativePath, 42, out var storedPhoto));
        Assert.Equal("image/jpeg", storedPhoto!.ContentType);

        var savedFile = Path.Combine(_root, "42", Path.GetFileName(result.RelativePath));
        Assert.True(File.Exists(savedFile));
        Assert.True(await storage.DeleteIfOwnedAsync(result.RelativePath, 42));
        Assert.False(File.Exists(savedFile));
        Assert.False(storage.IsOwnedPath(result.RelativePath, 42));
    }

    [Fact]
    public async Task IsOwnedPath_RejectsExternalAndMalformedUrls()
    {
        var storage = new TrophyPhotoStorage(_root);
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        var result = await storage.SaveAsync(
            CreateFormFile(bytes, "photo.png", "image/png"),
            42
        );

        Assert.False(storage.IsOwnedPath($"https://evil.example{result.RelativePath}", 42));
        Assert.False(storage.IsOwnedPath("/uploads/trophies/42/%ZZ.png", 42));
    }

    [Fact]
    public async Task SaveAsync_RejectsContentWithoutAnAllowedImageSignature()
    {
        var storage = new TrophyPhotoStorage(_root);
        var photo = CreateFormFile("not an image"u8.ToArray(), "fake.jpg", "image/jpeg");

        var error = await Assert.ThrowsAsync<InvalidDataException>(
            () => storage.SaveAsync(photo, 42)
        );

        Assert.Contains("JPEG", error.Message);
        Assert.Empty(Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories));
    }

    private static FormFile CreateFormFile(byte[] bytes, string fileName, string contentType)
    {
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "photo", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
