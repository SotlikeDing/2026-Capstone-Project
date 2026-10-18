using System.Text.Json;
using System.Text.RegularExpressions;

namespace WebAPI.Services;

/// <summary>
/// Farm photos are stored like advert images: a JSON array of base64 data URLs in
/// farm.farmImagesJson. Lists never carry the bytes; each photo is fetched on its own
/// through api/farmer/{farmId}/images/{index} (the website serves it as /plaasfoto/...).
/// </summary>
public static partial class FarmImages
{
    public const int MaxImages = 6;
    public const int MaxImageBytes = 2_500_000;

    [GeneratedRegex(@"^data:(image/(?:jpeg|png|webp));base64,([A-Za-z0-9+/=]+)$")]
    private static partial Regex DataUrl();

    // A photo the farm already has, as the website or API hands it out: ".../plaasfoto/12/0?v=.." or ".../images/0".
    [GeneratedRegex(@"(?:plaasfoto/(\d+)|api/farmer/(\d+)/images)/(\d+)(?:\?.*)?$")]
    private static partial Regex ExistingRef();

    /// <summary>The website path for photo <paramref name="index"/>; <paramref name="version"/> changes whenever the photos do.</summary>
    public static string WebPath(int farmId, int index, int version) => $"plaasfoto/{farmId}/{index}?v={version}";

    public static List<string> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? new(); }
        catch (JsonException) { return new(); }
    }

    /// <summary>
    /// Builds the stored JSON from what the client sent: new photos as data URLs, kept photos as
    /// references to this farm's existing ones. Throws ArgumentException with an Afrikaans message
    /// for anything else (other farms' photos, wrong types, too many or too large).
    /// </summary>
    public static string? BuildJson(int farmId, IEnumerable<string> requested, string? existingJson)
    {
        var existing = Parse(existingJson);
        var result = new List<string>();
        foreach (var raw in requested.Where(r => !string.IsNullOrWhiteSpace(r)))
        {
            var item = raw.Trim();
            var reference = ExistingRef().Match(item);
            if (reference.Success)
            {
                int refFarm = int.Parse(reference.Groups[1].Success ? reference.Groups[1].Value : reference.Groups[2].Value);
                int index = int.Parse(reference.Groups[3].Value);
                if (refFarm != farmId || index >= existing.Count)
                    throw new ArgumentException("Een van die plaasfoto's bestaan nie meer nie. Laai die blad weer en probeer weer.");
                result.Add(existing[index]);
                continue;
            }
            if (item.Contains("savanna_sunrise", StringComparison.OrdinalIgnoreCase))
                continue; // the placeholder shown for farms without photos
            var data = DataUrl().Match(item);
            if (!data.Success)
                throw new ArgumentException("Plaasfoto's moet JPG-, PNG- of WebP-prente wees.");
            if (data.Groups[2].Value.Length / 4 * 3 > MaxImageBytes)
                throw new ArgumentException("Elke plaasfoto moet kleiner as 2,5 MB wees.");
            result.Add(item);
        }
        if (result.Count > MaxImages)
            throw new ArgumentException($"'n Plaas kan hoogstens {MaxImages} foto's hê.");
        return result.Count == 0 ? null : JsonSerializer.Serialize(result);
    }

    /// <summary>Decodes photo <paramref name="index"/>, or null when there is none.</summary>
    public static (byte[] Bytes, string ContentType)? Decode(string? json, int index)
    {
        var images = Parse(json);
        if (index < 0 || index >= images.Count) return null;
        var match = DataUrl().Match(images[index]);
        if (!match.Success) return null;
        try { return (Convert.FromBase64String(match.Groups[2].Value), match.Groups[1].Value); }
        catch (FormatException) { return null; }
    }
}
