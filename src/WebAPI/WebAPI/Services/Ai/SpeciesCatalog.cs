using System.Text.Json;
using System.Text.RegularExpressions;

namespace WebAPI.Services.Ai;

/// <summary>One species in the reference catalogue (tools/ai/data/species_catalog.json): its published reference trophy-bull fee and how it is named in the app.</summary>
public sealed record SpeciesInfo(string Id, string Afrikaans, string English, double RefFeeUsd, bool Dangerous, IReadOnlyDictionary<string, double> Sources);

/// <summary>
/// Species and province name resolution shared by the feature encoder, the advisor and the learning harvest.
/// Must behave exactly like tools/ai/common.py (resolve_species / normalize_province) - pinned by the golden-vector tests.
/// </summary>
public sealed class SpeciesCatalog
{
    public static readonly string[] Provinces =
    {
        "limpopo", "gauteng", "mpumalanga", "north_west", "free_state",
        "northern_cape", "eastern_cape", "western_cape", "kwazulu_natal"
    };

    private static readonly Dictionary<string, string> ProvinceAliases = new()
    {
        ["limpopo"] = "limpopo", ["gauteng"] = "gauteng", ["mpumalanga"] = "mpumalanga",
        ["north west"] = "north_west", ["northwest"] = "north_west", ["noordwes"] = "north_west", ["noord-wes"] = "north_west", ["nw"] = "north_west",
        ["free state"] = "free_state", ["vrystaat"] = "free_state", ["fs"] = "free_state",
        ["northern cape"] = "northern_cape", ["noord-kaap"] = "northern_cape", ["noordkaap"] = "northern_cape", ["nc"] = "northern_cape",
        ["eastern cape"] = "eastern_cape", ["oos-kaap"] = "eastern_cape", ["ooskaap"] = "eastern_cape", ["ec"] = "eastern_cape",
        ["western cape"] = "western_cape", ["wes-kaap"] = "western_cape", ["weskaap"] = "western_cape", ["wc"] = "western_cape",
        ["kwazulu-natal"] = "kwazulu_natal", ["kwazulu natal"] = "kwazulu_natal", ["kzn"] = "kwazulu_natal", ["natal"] = "kwazulu_natal",
    };

    private readonly Dictionary<string, SpeciesInfo> _byAlias = new(StringComparer.Ordinal);

    public IReadOnlyList<SpeciesInfo> All { get; }

    private SpeciesCatalog(List<SpeciesInfo> all, Dictionary<string, SpeciesInfo> byAlias)
    {
        All = all;
        _byAlias = byAlias;
    }

    /// <summary>Loads species_catalog.json from the model bundle.</summary>
    public static SpeciesCatalog Load(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var all = new List<SpeciesInfo>();
        var byAlias = new Dictionary<string, SpeciesInfo>(StringComparer.Ordinal);

        foreach (var sp in doc.RootElement.GetProperty("species").EnumerateArray())
        {
            var sources = new Dictionary<string, double>();
            foreach (var s in sp.GetProperty("sources").EnumerateObject())
                sources[s.Name] = s.Value.GetDouble();

            var info = new SpeciesInfo(
                sp.GetProperty("id").GetString()!,
                sp.GetProperty("af").GetString()!,
                sp.GetProperty("en").GetString()!,
                sp.GetProperty("refFeeUsd").GetDouble(),
                sp.GetProperty("dangerous").GetBoolean(),
                sources);
            all.Add(info);

            byAlias[info.Id] = info;
            byAlias[info.Afrikaans.ToLowerInvariant()] = info;
            byAlias[info.English.ToLowerInvariant()] = info;
            if (sp.TryGetProperty("aliases", out var aliases))
                foreach (var a in aliases.EnumerateArray())
                    byAlias[a.GetString()!.ToLowerInvariant()] = info;
        }
        return new SpeciesCatalog(all, byAlias);
    }

    /// <summary>Returns the catalogue entry for a free-text species name (Afrikaans or English, either spelling used in the app), or null when unknown.</summary>
    public SpeciesInfo? Resolve(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var key = Regex.Replace(name.Trim().ToLowerInvariant(), @"\s+", " ");
        return _byAlias.TryGetValue(key, out var sp) ? sp : null;
    }

    /// <summary>Maps free-text province names (English or Afrikaans, any casing) to the canonical province id; unknown/blank falls back to limpopo, the same default the training data uses.</summary>
    public static string NormalizeProvince(string? value) => TryNormalizeProvince(value) ?? "limpopo";

    /// <summary>Like <see cref="NormalizeProvince"/>, but null when the text is blank or not a recognisable province (no default).</summary>
    public static string? TryNormalizeProvince(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var key = Regex.Replace(value.Trim().ToLowerInvariant().Replace('_', ' '), @"\s+", " ");
        var underscored = key.Replace(' ', '_');
        if (Array.IndexOf(Provinces, underscored) >= 0) return underscored;
        if (ProvinceAliases.TryGetValue(key, out var p)) return p;
        if (ProvinceAliases.TryGetValue(key.Replace(" ", ""), out p)) return p;
        return null;
    }
}
