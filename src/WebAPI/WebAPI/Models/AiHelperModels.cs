namespace WebAPI.Models;

/// <summary>
/// One optimisation request: the role's current draft offer (package / day-rate / video package / farm rate / ad) plus
/// context. Prices in <see cref="Draft"/> are in <see cref="Currency"/>; the response uses the same currency.
/// </summary>
public class AiOptimizeRequest
{
    /// <summary>PJ, PH, VG, FARMER (alias BOER) or AP. The caller must actually hold this role (Admin may use any).</summary>
    public string RoleCode { get; set; } = "";
    public string? Province { get; set; }
    /// <summary>"USD" or "ZAR".</summary>
    public string Currency { get; set; } = "USD";
    /// <summary>"international" (USD-quoting overseas clients) or "domestic" (ZAR, local hunters). Null = USD -> international, ZAR -> domestic.</summary>
    public string? Market { get; set; }
    /// <summary>"profit", "sales" or "balanced".</summary>
    public string Goal { get; set; } = "balanced";
    /// <summary>Start month of the offer (1-12); null = current month.</summary>
    public int? Month { get; set; }
    public AiDraft Draft { get; set; } = new();
}

/// <summary>The offer being optimised. One flat shape for every role; each role reads only the fields that apply to it.</summary>
public class AiDraft
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    /// <summary>PJ: price per hunter for the whole package. PH: day rate. VG: total package price. FARMER: per-hunter daily rate. AP: not used.</summary>
    public decimal? Price { get; set; }
    /// <summary>PJ only: the observer (non-hunter) price for the same package, to check the observer/hunter ratio.</summary>
    public decimal? ObserverPrice { get; set; }
    public int? DurationDays { get; set; }
    /// <summary>1 = one hunter per PH (1x1), 2 = two hunters per PH (2x1).</summary>
    public int? HuntersPerPh { get; set; }
    /// <summary>PJ: species bundled into the package price. FARMER: species the farm offers.</summary>
    public List<string> Species { get; set; } = new();
    /// <summary>PJ services keys: ph_included, accommodation, meals, field_prep, airport_transfer, videography, drone.</summary>
    public List<string> Services { get; set; } = new();

    // PH / VG
    public int? YearsExperience { get; set; }
    public bool DangerousGameRate { get; set; }
    public bool DangerousGameAccredited { get; set; }
    public double? RatingAverage { get; set; }
    public int? RatingCount { get; set; }
    public int? HuntsCompleted { get; set; }
    /// <summary>VG: "basic", "standard" or "documentary".</summary>
    public string? VgTier { get; set; }
    public bool Drone { get; set; }
    public bool Uhd4k { get; set; }
    public int? CrewSize { get; set; }

    // FARMER
    public double? FarmSizeHa { get; set; }
    public int? LodgeRooms { get; set; }
    public bool Catering { get; set; } = true;
    public bool ColdRoom { get; set; }
    public bool GuideIncluded { get; set; } = true;
    public bool Pool { get; set; }
    public bool Wifi { get; set; }
    public bool GameFenced { get; set; }
    public List<AiSpeciesPriceDraft> SpeciesPricing { get; set; } = new();

    // AP
    public string? AdCategory { get; set; }
    public string? AdPlacement { get; set; }
}

/// <summary>A farm's current price for one species and tier (FARMER role).</summary>
public class AiSpeciesPriceDraft
{
    public string Species { get; set; } = "";
    /// <summary>"female", "male" or "trophy".</summary>
    public string Tier { get; set; } = "trophy";
    public decimal? Price { get; set; }
}

/// <summary>Everything the AI Helper answers with: the model's numbers, comparables, the LLM's written advice and transparency metadata.</summary>
public class AiOptimizeResponse
{
    public string RoleCode { get; set; } = "";
    public string Currency { get; set; } = "USD";
    public string Market { get; set; } = "international";
    public decimal UsdToZarRate { get; set; }
    public AiPriceAssessment? Price { get; set; }
    public List<AiSpeciesAssessment> SpeciesPrices { get; set; } = new();
    public AiAdBenchmark? AdBenchmark { get; set; }
    public List<AiComparable> Comparables { get; set; } = new();
    public AiNarrative? Narrative { get; set; }
    /// <summary>"ok", "unavailable" (LLM failed - numbers are still valid) or "disabled".</summary>
    public string NarrativeStatus { get; set; } = "disabled";
    public string? NarrativeError { get; set; }
    public AiEngineInfo Engine { get; set; } = new();
    /// <summary>Id of the logged advice, used to attach thumbs-up/down feedback (0 if logging failed).</summary>
    public long AdviceId { get; set; }
    /// <summary>Plain-language warnings about the input (unknown species, DG rate without accreditation, ...).</summary>
    public List<string> Notes { get; set; } = new();
}

/// <summary>The price model's verdict on one price.</summary>
public class AiPriceAssessment
{
    public string Label { get; set; } = "";
    public decimal? Current { get; set; }
    public decimal P10 { get; set; }
    public decimal P50 { get; set; }
    public decimal P90 { get; set; }
    /// <summary>"below" (under the P10 - likely underpriced), "within" or "above" (over the P90). "unknown" when no current price was given.</summary>
    public string Position { get; set; } = "unknown";
    /// <summary>Current price vs the model's P50, in percent (positive = above the model).</summary>
    public decimal? DeltaPercent { get; set; }
    public string Confidence { get; set; } = "low";
    public string Evidence { get; set; } = "assumed";
}

public class AiSpeciesAssessment
{
    public string Species { get; set; } = "";
    public string Tier { get; set; } = "trophy";
    public bool KnownSpecies { get; set; }
    public AiPriceAssessment? Price { get; set; }
}

/// <summary>Advertiser view: the caller's ads vs platform averages, computed from real view/click counts (no model involved).</summary>
public class AiAdBenchmark
{
    public string? Category { get; set; }
    public int OwnAdCount { get; set; }
    public long OwnViews { get; set; }
    public long OwnClicks { get; set; }
    public double? OwnClickThroughPercent { get; set; }
    public int CategoryAdCount { get; set; }
    public double? CategoryClickThroughPercent { get; set; }
    public double? PlatformClickThroughPercent { get; set; }
    public List<AiOwnAd> OwnAds { get; set; } = new();
}

public class AiOwnAd
{
    public string Title { get; set; } = "";
    public string? Category { get; set; }
    public long Views { get; set; }
    public long Clicks { get; set; }
    public double? ClickThroughPercent { get; set; }
}

/// <summary>A similar listing already on the platform, shown so the advice is checkable. No owner identity is included.</summary>
public class AiComparable
{
    public string Kind { get; set; } = "";
    public string Title { get; set; } = "";
    public decimal? Price { get; set; }
    public string? PriceNote { get; set; }
    public int? DurationDays { get; set; }
    public string? Province { get; set; }
}

/// <summary>The LLM's written advice (Afrikaans). Contains no prices: those come only from the price model.</summary>
public class AiNarrative
{
    public string Headline { get; set; } = "";
    public List<string> TitleSuggestions { get; set; } = new();
    public string? DescriptionRewrite { get; set; }
    public List<AiInclusionSuggestion> InclusionSuggestions { get; set; } = new();
    public List<AiUpsell> Upsells { get; set; } = new();
    public string? SeasonalAdvice { get; set; }
    public List<AiTip> Tips { get; set; } = new();
    public List<string> Risks { get; set; } = new();
}

public class AiInclusionSuggestion
{
    public string Item { get; set; } = "";
    public string Why { get; set; } = "";
    /// <summary>"hoog", "medium" or "laag".</summary>
    public string Impact { get; set; } = "medium";
}

public class AiUpsell
{
    public string Name { get; set; } = "";
    public string Why { get; set; } = "";
}

public class AiTip
{
    public string Title { get; set; } = "";
    public string Detail { get; set; } = "";
    /// <summary>1 = most important.</summary>
    public int Priority { get; set; } = 2;
}

/// <summary>Which engines produced this answer, and how much evidence stands behind them.</summary>
public class AiEngineInfo
{
    public string? ModelVersion { get; set; }
    public string? PriceEvidence { get; set; }
    public string? LlmProvider { get; set; }
    public string? LlmModel { get; set; }
    public long LatencyMs { get; set; }
}

public class AiFeedbackRequest
{
    public long AdviceId { get; set; }
    public bool Helpful { get; set; }
    public string? Comment { get; set; }
}

// ============================================================================ Smart Teaming

/// <summary>"Find me team partners." A PJ looks for PH / VG / Boer partners; a PH, VG or Boer looks for outfitters (PJ) to join. AP is excluded.</summary>
public class AiTeamRequest
{
    /// <summary>PJ, PH, VG or FARMER (alias BOER). AP is accepted but answered with an exclusion notice.</summary>
    public string RoleCode { get; set; } = "";
    /// <summary>The area the user works in. Null = derived from the user's own listings (farms, packages, regions).</summary>
    public string? Province { get; set; }
    /// <summary>PJ only: the most the PJ wants to pay per day (ZAR) for a PH / videographer / farm. Null = no budget filter.</summary>
    public decimal? MaxDailyRate { get; set; }
    /// <summary>PJ only: which partner roles to look for (PH, VG, FARMER). Empty = all three.</summary>
    public List<string> WantRoles { get; set; } = new();
    public int Take { get; set; } = 8;
}

public class AiTeamResponse
{
    public string RoleCode { get; set; } = "";
    /// <summary>True for AP: advertisers do not take part in field teams.</summary>
    public bool Excluded { get; set; }
    public string? ExclusionMessage { get; set; }
    /// <summary>The provinces the match was scored against (canonical ids).</summary>
    public List<string> ViewerProvinces { get; set; } = new();
    public AiTeamModelInfo Model { get; set; } = new();
    public List<AiTeamMatch> Matches { get; set; } = new();
    /// <summary>LLM-written one-paragraph overview (Afrikaans). Null when the LLM is unavailable.</summary>
    public string? Summary { get; set; }
    /// <summary>"ok", "unavailable" or "disabled" - same meaning as for price advice.</summary>
    public string NarrativeStatus { get; set; } = "disabled";
    public string? NarrativeError { get; set; }
    public AiEngineInfo Engine { get; set; } = new();
}

/// <summary>How the match score was produced: the plan's prior weights, blended with a model learned from real invite outcomes once there are enough.</summary>
public class AiTeamModelInfo
{
    /// <summary>"prior" (no/too few real outcomes yet) or "prior+learned".</summary>
    public string Source { get; set; } = "prior";
    /// <summary>Invites that were accepted or declined, plus existing team rows - the training labels.</summary>
    public int LabelledPairs { get; set; }
    /// <summary>Share of the score that comes from the learned model (0 - 0.8), grows with LabelledPairs.</summary>
    public double LearnedShare { get; set; }
    /// <summary>Prior weight per component key (proximity, credentials, rate, history).</summary>
    public Dictionary<string, double> PriorWeights { get; set; } = new();
    /// <summary>Learned logistic-regression coefficient per component (null until learned).</summary>
    public Dictionary<string, double>? LearnedCoefficients { get; set; }
}

public class AiTeamMatch
{
    public int UserId { get; set; }
    public int UserRoleId { get; set; }
    public string Name { get; set; } = "";
    public string? BusinessName { get; set; }
    /// <summary>PJ, PH, VG or FARMER.</summary>
    public string RoleCode { get; set; } = "";
    public List<string> Provinces { get; set; } = new();
    /// <summary>0-100.</summary>
    public int Score { get; set; }
    public List<AiTeamComponent> Components { get; set; } = new();
    /// <summary>Plain facts behind the score (Afrikaans), safe to show.</summary>
    public List<string> Facts { get; set; } = new();
    /// <summary>LLM-written "why this partner" (Afrikaans). Null when the LLM is unavailable.</summary>
    public string? Reason { get; set; }
    public double? RatingAverage { get; set; }
    public int RatingCount { get; set; }
    public long YearsExperience { get; set; }
    public decimal? DailyRate { get; set; }
    public int CompletedHunts { get; set; }
    public int JointHunts { get; set; }
    /// <summary>"none", "team" (already in the PJ's team), "invite_pending", "invite_declined" or "invite_accepted".</summary>
    public string Relationship { get; set; } = "none";
    /// <summary>Whether the caller (a PJ) can send this candidate a team invite right now.</summary>
    public bool CanInvite { get; set; }
}

public class AiTeamComponent
{
    /// <summary>proximity, credentials, rate or history.</summary>
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    /// <summary>0-1.</summary>
    public double Value { get; set; }
    public double Weight { get; set; }
}
