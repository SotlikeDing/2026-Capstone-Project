namespace MobileApi.Models.RowlandWard;

public static class LegacyTrophySpeciesAuditStates
{
    public const string SafeExactMatch = "SAFE_EXACT_MATCH";
    public const string SafeAliasMatch = "SAFE_ALIAS_MATCH";
    public const string LikelyFuzzyMatchReviewRequired = "LIKELY_FUZZY_MATCH_REVIEW_REQUIRED";
    public const string Ambiguous = "AMBIGUOUS";
    public const string NoMatch = "NO_MATCH";
}

public static class LegacyTrophySpeciesAuditSources
{
    public const string NormalizedExact = "NORMALIZED_EXACT";
    public const string ApprovedAlias = "APPROVED_ALIAS";
    public const string FuzzyReview = "FUZZY_REVIEW";
    public const string None = "NONE";
}

public sealed record LegacyTrophySpeciesValue(
    string OriginalSpeciesText,
    long TrophyCount
);

public sealed record LegacyTrophySpeciesAuditCandidate(
    string ClassId,
    string DisplayNameAf,
    int? Confidence
);

public sealed record LegacyTrophySpeciesAuditResult(
    string OriginalSpeciesText,
    string NormalizedSpeciesText,
    long TrophyCount,
    string MatchState,
    string MatchSource,
    LegacyTrophySpeciesAuditCandidate? ProposedCandidate,
    IReadOnlyList<LegacyTrophySpeciesAuditCandidate> Candidates,
    bool ManualReviewRequired
);

public sealed record LegacyTrophySpeciesAuditResponse(
    DateTimeOffset GeneratedAtUtc,
    int DistinctLegacySpeciesCount,
    long LegacyTrophyCount,
    IReadOnlyList<LegacyTrophySpeciesAuditResult> Results
);
