namespace WebApp.Models
{
    // Client-side mirrors of WebAPI/Models/AiHelperModels.cs (JSON is camelCase on the wire; System.Text.Json web defaults bind it).

    /// <summary>One AI Helper request: the role's draft offer plus context. Prices are in <see cref="Currency"/>.</summary>
    public class AiOptimizeRequestModel
    {
        public string RoleCode { get; set; } = "PJ";
        public string? Province { get; set; }
        public string Currency { get; set; } = "USD";
        /// <summary>"international", "domestic" or null (server picks: USD -> international, ZAR -> domestic).</summary>
        public string? Market { get; set; }
        public string Goal { get; set; } = "balanced";
        public int? Month { get; set; }
        public AiDraftModel Draft { get; set; } = new();
    }

    public class AiDraftModel
    {
        public string? Title { get; set; }
        public string? Description { get; set; }
        public decimal? Price { get; set; }
        public decimal? ObserverPrice { get; set; }
        public int? DurationDays { get; set; }
        public int? HuntersPerPh { get; set; }
        public List<string> Species { get; set; } = new();
        public List<string> Services { get; set; } = new();
        public int? YearsExperience { get; set; }
        public bool DangerousGameRate { get; set; }
        public bool DangerousGameAccredited { get; set; }
        public double? RatingAverage { get; set; }
        public int? RatingCount { get; set; }
        public int? HuntsCompleted { get; set; }
        public string? VgTier { get; set; }
        public bool Drone { get; set; }
        public bool Uhd4k { get; set; }
        public int? CrewSize { get; set; }
        public double? FarmSizeHa { get; set; }
        public int? LodgeRooms { get; set; }
        public bool Catering { get; set; } = true;
        public bool ColdRoom { get; set; }
        public bool GuideIncluded { get; set; } = true;
        public bool Pool { get; set; }
        public bool Wifi { get; set; }
        public bool GameFenced { get; set; }
        public List<AiSpeciesPriceDraftModel> SpeciesPricing { get; set; } = new();
        public string? AdCategory { get; set; }
        public string? AdPlacement { get; set; }
    }

    public class AiSpeciesPriceDraftModel
    {
        public string Species { get; set; } = "";
        public string Tier { get; set; } = "trophy";
        public decimal? Price { get; set; }
    }

    public class AiOptimizeResponseModel
    {
        public string RoleCode { get; set; } = "";
        public string Currency { get; set; } = "USD";
        public string Market { get; set; } = "international";
        public decimal UsdToZarRate { get; set; }
        public AiPriceAssessmentModel? Price { get; set; }
        public List<AiSpeciesAssessmentModel> SpeciesPrices { get; set; } = new();
        public AiAdBenchmarkModel? AdBenchmark { get; set; }
        public List<AiComparableModel> Comparables { get; set; } = new();
        public AiNarrativeModel? Narrative { get; set; }
        public string NarrativeStatus { get; set; } = "disabled";
        public string? NarrativeError { get; set; }
        public AiEngineInfoModel Engine { get; set; } = new();
        public long AdviceId { get; set; }
        public List<string> Notes { get; set; } = new();
    }

    public class AiPriceAssessmentModel
    {
        public string Label { get; set; } = "";
        public decimal? Current { get; set; }
        public decimal P10 { get; set; }
        public decimal P50 { get; set; }
        public decimal P90 { get; set; }
        public string Position { get; set; } = "unknown";
        public decimal? DeltaPercent { get; set; }
        public string Confidence { get; set; } = "low";
        public string Evidence { get; set; } = "assumed";
    }

    public class AiSpeciesAssessmentModel
    {
        public string Species { get; set; } = "";
        public string Tier { get; set; } = "trophy";
        public bool KnownSpecies { get; set; }
        public AiPriceAssessmentModel? Price { get; set; }
    }

    public class AiAdBenchmarkModel
    {
        public string? Category { get; set; }
        public int OwnAdCount { get; set; }
        public long OwnViews { get; set; }
        public long OwnClicks { get; set; }
        public double? OwnClickThroughPercent { get; set; }
        public int CategoryAdCount { get; set; }
        public double? CategoryClickThroughPercent { get; set; }
        public double? PlatformClickThroughPercent { get; set; }
        public List<AiOwnAdModel> OwnAds { get; set; } = new();
    }

    public class AiOwnAdModel
    {
        public string Title { get; set; } = "";
        public string? Category { get; set; }
        public long Views { get; set; }
        public long Clicks { get; set; }
        public double? ClickThroughPercent { get; set; }
    }

    public class AiComparableModel
    {
        public string Kind { get; set; } = "";
        public string Title { get; set; } = "";
        public decimal? Price { get; set; }
        public string? PriceNote { get; set; }
        public int? DurationDays { get; set; }
        public string? Province { get; set; }
    }

    public class AiNarrativeModel
    {
        public string Headline { get; set; } = "";
        public List<string> TitleSuggestions { get; set; } = new();
        public string? DescriptionRewrite { get; set; }
        public List<AiInclusionSuggestionModel> InclusionSuggestions { get; set; } = new();
        public List<AiUpsellModel> Upsells { get; set; } = new();
        public string? SeasonalAdvice { get; set; }
        public List<AiTipModel> Tips { get; set; } = new();
        public List<string> Risks { get; set; } = new();
    }

    public class AiInclusionSuggestionModel { public string Item { get; set; } = ""; public string Why { get; set; } = ""; public string Impact { get; set; } = "medium"; }
    public class AiUpsellModel { public string Name { get; set; } = ""; public string Why { get; set; } = ""; }
    public class AiTipModel { public string Title { get; set; } = ""; public string Detail { get; set; } = ""; public int Priority { get; set; } = 2; }

    public class AiEngineInfoModel
    {
        public string? ModelVersion { get; set; }
        public string? PriceEvidence { get; set; }
        public string? LlmProvider { get; set; }
        public string? LlmModel { get; set; }
        public long LatencyMs { get; set; }
    }

    // ------------------------------------------------------------------ Smart Teaming

    public class AiTeamRequestModel
    {
        public string RoleCode { get; set; } = "PJ";
        public string? Province { get; set; }
        public decimal? MaxDailyRate { get; set; }
        public List<string> WantRoles { get; set; } = new();
        public int Take { get; set; } = 8;
    }

    public class AiTeamResponseModel
    {
        public string RoleCode { get; set; } = "";
        public bool Excluded { get; set; }
        public string? ExclusionMessage { get; set; }
        public List<string> ViewerProvinces { get; set; } = new();
        public AiTeamModelInfoModel Model { get; set; } = new();
        public List<AiTeamMatchModel> Matches { get; set; } = new();
        public string? Summary { get; set; }
        public string NarrativeStatus { get; set; } = "disabled";
        public string? NarrativeError { get; set; }
        public AiEngineInfoModel Engine { get; set; } = new();
    }

    public class AiTeamModelInfoModel
    {
        public string Source { get; set; } = "prior";
        public int LabelledPairs { get; set; }
        public double LearnedShare { get; set; }
        public Dictionary<string, double> PriorWeights { get; set; } = new();
        public Dictionary<string, double>? LearnedCoefficients { get; set; }
    }

    public class AiTeamMatchModel
    {
        public int UserId { get; set; }
        public int UserRoleId { get; set; }
        public string Name { get; set; } = "";
        public string? BusinessName { get; set; }
        public string RoleCode { get; set; } = "";
        public List<string> Provinces { get; set; } = new();
        public int Score { get; set; }
        public List<AiTeamComponentModel> Components { get; set; } = new();
        public List<string> Facts { get; set; } = new();
        public string? Reason { get; set; }
        public double? RatingAverage { get; set; }
        public int RatingCount { get; set; }
        public long YearsExperience { get; set; }
        public decimal? DailyRate { get; set; }
        public int CompletedHunts { get; set; }
        public int JointHunts { get; set; }
        public string Relationship { get; set; } = "none";
        public bool CanInvite { get; set; }
    }

    public class AiTeamComponentModel
    {
        public string Key { get; set; } = "";
        public string Label { get; set; } = "";
        public double Value { get; set; }
        public double Weight { get; set; }
    }

    // ------------------------------------------------------------------ packet maker hand-off

    /// <summary>
    /// The offer the user assembled from the AI Helper's advice, waiting to be opened in the normal editor for that role
    /// (package, video package, farm, advertisement). The editor pre-fills its form from it; nothing is saved until the user
    /// reviews it and clicks the editor's own save button.
    /// </summary>
    public class AiAppliedDraft
    {
        /// <summary>PJ, VG, FARMER or AP.</summary>
        public string RoleCode { get; set; } = "";
        /// <summary>The existing package / farm / ad being updated; 0 = create a new one.</summary>
        public int TargetId { get; set; }
        public string? Title { get; set; }
        public string? Description { get; set; }
        /// <summary>PJ: price per hunter. VG: day rate. FARMER: daily rate per hunter. AP: not used.</summary>
        public decimal? Price { get; set; }
        public decimal? ObserverPrice { get; set; }
        public int? DurationDays { get; set; }
        public string? Province { get; set; }
        public string? AdCategory { get; set; }
        /// <summary>FARMER: species price changes to apply to matching inventory rows.</summary>
        public List<AiSpeciesPriceDraftModel> SpeciesPrices { get; set; } = new();
    }
}
