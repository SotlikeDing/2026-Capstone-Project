using System.Text.Json.Serialization;

namespace WebAPI.Services.Ai;

/// <summary>
/// The model input ("raw" row) in the exact snake_case schema the Python training pipeline uses (tools/ai/common.py),
/// so the same JSON is used for golden-vector tests, the learning harvest (aiMarketObservations.rawJson) and live scoring.
/// Every field is optional; missing values fall back to the same defaults the trainer uses.
/// </summary>
public class PriceRawInput
{
    [JsonPropertyName("province")] public string? Province { get; set; }
    [JsonPropertyName("month")] public int? Month { get; set; }
    /// <summary>"international" (USD-quoting overseas clients) or "domestic" (ZAR, local hunters).</summary>
    [JsonPropertyName("market")] public string? Market { get; set; }

    // PJ_PACKAGE
    [JsonPropertyName("duration_days")] public double? DurationDays { get; set; }
    [JsonPropertyName("hunters_per_ph")] public int? HuntersPerPh { get; set; }
    [JsonPropertyName("services")] public Dictionary<string, bool>? Services { get; set; }
    [JsonPropertyName("included_species")] public List<string>? IncludedSpecies { get; set; }

    // PH_RATE
    [JsonPropertyName("years_experience")] public double? YearsExperience { get; set; }
    [JsonPropertyName("rating_count")] public double? RatingCount { get; set; }
    [JsonPropertyName("rating_avg")] public double? RatingAvg { get; set; }
    [JsonPropertyName("hunts_completed")] public double? HuntsCompleted { get; set; }
    [JsonPropertyName("is_dg_rate")] public bool? IsDgRate { get; set; }
    [JsonPropertyName("dg_accredited")] public bool? DgAccredited { get; set; }

    // VG_PACKAGE
    [JsonPropertyName("days_covered")] public double? DaysCovered { get; set; }
    [JsonPropertyName("tier")] public string? Tier { get; set; }
    [JsonPropertyName("drone")] public bool? Drone { get; set; }
    [JsonPropertyName("uhd_4k")] public bool? Uhd4k { get; set; }
    [JsonPropertyName("crew_size")] public int? CrewSize { get; set; }

    // FARM_DAILY_RATE + SPECIES_FEE
    [JsonPropertyName("farm_size_ha")] public double? FarmSizeHa { get; set; }
    [JsonPropertyName("lodge_rooms")] public double? LodgeRooms { get; set; }
    [JsonPropertyName("catering")] public bool? Catering { get; set; }
    [JsonPropertyName("cold_room")] public bool? ColdRoom { get; set; }
    [JsonPropertyName("guide_included")] public bool? GuideIncluded { get; set; }
    [JsonPropertyName("pool")] public bool? Pool { get; set; }
    [JsonPropertyName("wifi")] public bool? Wifi { get; set; }
    [JsonPropertyName("n_species")] public double? NSpecies { get; set; }
    [JsonPropertyName("game_fenced")] public bool? GameFenced { get; set; }
    [JsonPropertyName("species")] public string? Species { get; set; }
}

/// <summary>The five offer kinds the price model covers.</summary>
public static class PriceKinds
{
    public const string PjPackage = "PJ_PACKAGE";
    public const string PhRate = "PH_RATE";
    public const string VgPackage = "VG_PACKAGE";
    public const string FarmDailyRate = "FARM_DAILY_RATE";
    public const string SpeciesFee = "SPECIES_FEE";
}

/// <summary>
/// Turns a <see cref="PriceRawInput"/> into the named numeric features the ONNX models were trained on. This is a line-by-line
/// mirror of tools/ai/common.py featurize(): change one only together with the other. AiFeatureParityTests replays the
/// trainer's golden vectors against this class and the real ONNX models to catch any drift.
/// </summary>
public sealed class FeatureEncoder
{
    private static readonly Dictionary<string, double> Tiers = new() { ["basic"] = 0, ["standard"] = 1, ["documentary"] = 2 };
    private readonly SpeciesCatalog _catalog;

    public FeatureEncoder(SpeciesCatalog catalog) => _catalog = catalog;

    // Python's `x or default` treats 0 like "missing"; these helpers reproduce that so both encoders agree on edge cases.
    private static double Or(double? v, double dflt) => v is null or 0 ? dflt : v.Value;
    private static double Num(double? v) => v ?? 0;
    private static double B(bool v) => v ? 1.0 : 0.0;
    private static bool Flag(bool? v, bool dflt) => v ?? dflt;

    /// <summary>Encodes one row; the returned dictionary holds every feature any kind can use, keyed by the names in feature_spec.json.</summary>
    public Dictionary<string, double> Encode(string kind, PriceRawInput raw)
    {
        var f = new Dictionary<string, double>();

        var prov = SpeciesCatalog.NormalizeProvince(raw.Province);
        int month = Math.Clamp((int)(raw.Month is null or 0 ? 6 : raw.Month.Value), 1, 12);
        foreach (var p in SpeciesCatalog.Provinces) f[$"prov_{p}"] = p == prov ? 1.0 : 0.0;
        f["month_sin"] = Math.Sin(2 * Math.PI * (month - 1) / 12.0);
        f["month_cos"] = Math.Cos(2 * Math.PI * (month - 1) / 12.0);
        f["peak_season"] = month is >= 5 and <= 8 ? 1.0 : 0.0;
        f["market_domestic"] = string.Equals((raw.Market ?? "international").Trim(), "domestic", StringComparison.OrdinalIgnoreCase) ? 1.0 : 0.0;

        var svc = raw.Services ?? new Dictionary<string, bool>();
        bool Svc(string key, bool dflt) => svc.TryGetValue(key, out var v) ? v : dflt;

        switch (kind)
        {
            case PriceKinds.PjPackage:
            {
                var included = (raw.IncludedSpecies ?? new()).Select(_catalog.Resolve).Where(s => s is not null).Select(s => s!).ToList();
                f["duration_days"] = Or(raw.DurationDays, 5);
                f["hunters_per_ph"] = Or(raw.HuntersPerPh, 1) >= 2 ? 2.0 : 1.0;
                f["svc_ph_included"] = B(Svc("ph_included", true));
                f["svc_accommodation"] = B(Svc("accommodation", true));
                f["svc_meals"] = B(Svc("meals", true));
                f["svc_field_prep"] = B(Svc("field_prep", true));
                f["svc_airport_transfer"] = B(Svc("airport_transfer", false));
                f["svc_videography"] = B(Svc("videography", false));
                f["svc_drone"] = B(Svc("drone", false));
                f["included_fee_log"] = Math.Log(1 + included.Sum(s => s.RefFeeUsd));
                f["n_included"] = included.Count;
                f["has_dangerous"] = B(included.Any(s => s.Dangerous));
                break;
            }
            case PriceKinds.PhRate:
            {
                double n = Num(raw.RatingCount), avg = Num(raw.RatingAvg);
                f["years_experience"] = Num(raw.YearsExperience);
                f["has_rating"] = B(n >= 1 && avg > 0);
                f["rating_avg"] = n >= 1 ? avg : 0.0;
                f["rating_n_log"] = Math.Log(1 + n);
                f["hunts_log"] = Math.Log(1 + Num(raw.HuntsCompleted));
                f["is_dg_rate"] = B(Flag(raw.IsDgRate, false));
                f["dg_accredited"] = B(Flag(raw.DgAccredited, false));
                break;
            }
            case PriceKinds.VgPackage:
            {
                f["days_covered"] = Or(raw.DaysCovered, 3);
                f["drone"] = B(Flag(raw.Drone, false));
                var tierKey = string.IsNullOrEmpty(raw.Tier) ? "standard" : raw.Tier.ToLowerInvariant();
                f["tier_idx"] = Tiers.TryGetValue(tierKey, out var tierIdx) ? tierIdx : 1;
                f["crew_size"] = Or(raw.CrewSize, 1);
                f["uhd_4k"] = B(Flag(raw.Uhd4k, false));
                f["years_experience"] = Num(raw.YearsExperience);
                break;
            }
            case PriceKinds.FarmDailyRate:
            {
                f["hunters_per_ph"] = Or(raw.HuntersPerPh, 1) >= 2 ? 2.0 : 1.0;
                f["farm_size_log"] = Math.Log(1 + Or(raw.FarmSizeHa, 2000));
                f["lodge_rooms"] = Num(raw.LodgeRooms);
                f["catering"] = B(Flag(raw.Catering, true));
                f["cold_room"] = B(Flag(raw.ColdRoom, false));
                f["guide_included"] = B(Flag(raw.GuideIncluded, true));
                f["pool"] = B(Flag(raw.Pool, false));
                f["wifi"] = B(Flag(raw.Wifi, false));
                f["n_species"] = Num(raw.NSpecies);
                f["game_fenced"] = B(Flag(raw.GameFenced, false));
                break;
            }
            case PriceKinds.SpeciesFee:
            {
                var sp = _catalog.Resolve(raw.Species);
                var tier = (raw.Tier ?? "trophy").ToLowerInvariant();
                f["species_ref_log"] = sp is not null ? Math.Log(sp.RefFeeUsd) : Math.Log(1400.0);
                f["is_dangerous"] = B(sp?.Dangerous ?? false);
                f["tier_female"] = B(tier == "female");
                f["tier_male"] = B(tier == "male");
                f["tier_trophy"] = B(tier is not ("female" or "male"));
                f["farm_size_log"] = Math.Log(1 + Or(raw.FarmSizeHa, 2000));
                f["game_fenced"] = B(Flag(raw.GameFenced, false));
                break;
            }
            default:
                throw new ArgumentException($"Unknown price kind '{kind}'.", nameof(kind));
        }
        return f;
    }
}
