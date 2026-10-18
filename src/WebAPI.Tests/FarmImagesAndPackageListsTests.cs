using System.Text.Json;
using WebAPI.Database;
using WebAPI.Services;
using Xunit;

namespace WebAPI.Tests;

public class FarmImagesAndPackageListsTests
{
    private static readonly string Jpeg = "data:image/jpeg;base64," + Convert.ToBase64String(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3 });
    private static readonly string Png = "data:image/png;base64," + Convert.ToBase64String(new byte[] { 0x89, 0x50, 0x4E, 0x47 });

    [Fact]
    public void New_photos_are_stored_in_order()
    {
        var json = FarmImages.BuildJson(7, new[] { Jpeg, Png }, null);
        Assert.Equal(new[] { Jpeg, Png }, JsonSerializer.Deserialize<List<string>>(json!));
    }

    [Fact]
    public void Kept_photos_are_swapped_back_for_the_stored_ones_and_can_be_reordered()
    {
        var existing = JsonSerializer.Serialize(new[] { Jpeg, Png });
        var json = FarmImages.BuildJson(7, new[] { "plaasfoto/7/1?v=123", "http://webapi.example/api/farmer/7/images/0" }, existing);
        Assert.Equal(new[] { Png, Jpeg }, JsonSerializer.Deserialize<List<string>>(json!));
    }

    [Fact]
    public void Another_farms_photo_is_refused()
    {
        var existing = JsonSerializer.Serialize(new[] { Jpeg });
        Assert.Throws<ArgumentException>(() => FarmImages.BuildJson(7, new[] { "plaasfoto/8/0" }, existing));
    }

    [Theory]
    [InlineData("https://evil.example/x.jpg")]
    [InlineData("data:image/svg+xml;base64,PHN2Zz48L3N2Zz4=")]
    [InlineData("data:text/html;base64,PGgxPg==")]
    public void Anything_but_a_jpeg_png_or_webp_data_url_is_refused(string photo)
    {
        Assert.Throws<ArgumentException>(() => FarmImages.BuildJson(7, new[] { photo }, null));
    }

    [Fact]
    public void At_most_six_photos()
    {
        Assert.Throws<ArgumentException>(() => FarmImages.BuildJson(7, Enumerable.Repeat(Jpeg, 7), null));
    }

    [Fact]
    public void The_placeholder_and_an_empty_list_store_nothing()
    {
        Assert.Null(FarmImages.BuildJson(7, new[] { "images/savanna_sunrise.jpg" }, null));
        Assert.Null(FarmImages.BuildJson(7, Array.Empty<string>(), null));
    }

    [Fact]
    public void Decode_returns_the_bytes_and_type_or_null()
    {
        var json = JsonSerializer.Serialize(new[] { Jpeg, Png });
        var png = FarmImages.Decode(json, 1);
        Assert.Equal("image/png", png!.Value.ContentType);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png.Value.Bytes);
        Assert.Null(FarmImages.Decode(json, 2));
        Assert.Null(FarmImages.Decode(null, 0));
    }

    [Fact]
    public void Package_lists_keep_one_trimmed_item_per_line()
    {
        Assert.Equal("PH en spoorsnyer\nVerblyf", Package_db.NormaliseItemList(" - PH en spoorsnyer \r\n\r\n• Verblyf\n"));
        Assert.Null(Package_db.NormaliseItemList("  \n "));
        Assert.Null(Package_db.NormaliseItemList(null));
    }

    [Fact]
    public void Package_lists_are_capped()
    {
        var many = string.Join("\n", Enumerable.Range(1, 40).Select(i => new string('x', 200)));
        var result = Package_db.NormaliseItemList(many)!;
        var items = result.Split('\n');
        Assert.Equal(13, items.Length);                       // 13 x 150 + 12 line breaks = 1962
        Assert.All(items, i => Assert.Equal(150, i.Length)); // whole items only
        Assert.True(result.Length <= 2000);

        var thirtyFive = string.Join("\n", Enumerable.Range(1, 35).Select(i => $"Item {i}"));
        Assert.Equal(30, Package_db.NormaliseItemList(thirtyFive)!.Split('\n').Length);
    }
}
