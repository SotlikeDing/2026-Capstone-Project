using MobileApi.Database;
using MobileApi.Models.RowlandWard;

namespace MobileApi.Services;

public sealed class LegacyTrophySpeciesAuditService : ILegacyTrophySpeciesAuditService
{
    private const int MatchSuggestionLimit = 5;

    private readonly ILegacyTrophyAuditRepository _repository;
    private readonly IRowlandWardSpeciesResolver _resolver;

    public LegacyTrophySpeciesAuditService(
        ILegacyTrophyAuditRepository repository,
        IRowlandWardSpeciesResolver resolver)
    {
        _repository = repository;
        _resolver = resolver;
    }

    public async Task<LegacyTrophySpeciesAuditResponse> AuditAsync()
    {
        var legacyValues = await _repository.GetDistinctLegacySpeciesAsync();
        var results = legacyValues
            .Select(AuditValue)
            .ToList()
            .AsReadOnly();

        return new LegacyTrophySpeciesAuditResponse(
            DateTimeOffset.UtcNow,
            results.Count,
            results.Sum(result => result.TrophyCount),
            results);
    }

    private LegacyTrophySpeciesAuditResult AuditValue(LegacyTrophySpeciesValue legacyValue)
    {
        var match = _resolver.Match(legacyValue.OriginalSpeciesText, MatchSuggestionLimit);

        return match.Outcome switch
        {
            RowlandWardSpeciesResolver.Exact => CreateSafeResult(
                legacyValue,
                match,
                LegacyTrophySpeciesAuditStates.SafeExactMatch,
                LegacyTrophySpeciesAuditSources.NormalizedExact),
            RowlandWardSpeciesResolver.Alias => CreateSafeResult(
                legacyValue,
                match,
                LegacyTrophySpeciesAuditStates.SafeAliasMatch,
                LegacyTrophySpeciesAuditSources.ApprovedAlias),
            RowlandWardSpeciesResolver.HighConfidenceFuzzy => CreateFuzzyResult(
                legacyValue,
                match),
            RowlandWardSpeciesResolver.Ambiguous => CreateReviewResult(
                legacyValue,
                match,
                LegacyTrophySpeciesAuditStates.Ambiguous,
                LegacyTrophySpeciesAuditSources.FuzzyReview),
            _ => CreateReviewResult(
                legacyValue,
                match,
                LegacyTrophySpeciesAuditStates.NoMatch,
                match.Suggestions.Count > 0
                    ? LegacyTrophySpeciesAuditSources.FuzzyReview
                    : LegacyTrophySpeciesAuditSources.None)
        };
    }

    private static LegacyTrophySpeciesAuditResult CreateSafeResult(
        LegacyTrophySpeciesValue legacyValue,
        RowlandWardSpeciesMatchResult match,
        string state,
        string source)
    {
        var candidate = match.Match is null
            ? null
            : new LegacyTrophySpeciesAuditCandidate(
                match.Match.Id,
                match.Match.DisplayNameAf,
                100);

        return new LegacyTrophySpeciesAuditResult(
            legacyValue.OriginalSpeciesText,
            match.NormalizedInput,
            legacyValue.TrophyCount,
            state,
            source,
            candidate,
            candidate is null ? [] : [candidate],
            false);
    }

    private static LegacyTrophySpeciesAuditResult CreateFuzzyResult(
        LegacyTrophySpeciesValue legacyValue,
        RowlandWardSpeciesMatchResult match)
    {
        var candidates = ToCandidates(match.Suggestions);
        var confidence = candidates
            .FirstOrDefault(candidate => candidate.ClassId == match.Match?.Id)
            ?.Confidence;
        var proposedCandidate = match.Match is null
            ? null
            : new LegacyTrophySpeciesAuditCandidate(
                match.Match.Id,
                match.Match.DisplayNameAf,
                confidence);

        return new LegacyTrophySpeciesAuditResult(
            legacyValue.OriginalSpeciesText,
            match.NormalizedInput,
            legacyValue.TrophyCount,
            LegacyTrophySpeciesAuditStates.LikelyFuzzyMatchReviewRequired,
            LegacyTrophySpeciesAuditSources.FuzzyReview,
            proposedCandidate,
            candidates,
            true);
    }

    private static LegacyTrophySpeciesAuditResult CreateReviewResult(
        LegacyTrophySpeciesValue legacyValue,
        RowlandWardSpeciesMatchResult match,
        string state,
        string source) => new(
            legacyValue.OriginalSpeciesText,
            match.NormalizedInput,
            legacyValue.TrophyCount,
            state,
            source,
            null,
            ToCandidates(match.Suggestions),
            true);

    private static IReadOnlyList<LegacyTrophySpeciesAuditCandidate> ToCandidates(
        IReadOnlyList<RowlandWardSpeciesSuggestion> suggestions) => suggestions
            .Select(suggestion => new LegacyTrophySpeciesAuditCandidate(
                suggestion.ClassId,
                suggestion.DisplayNameAf,
                suggestion.Confidence))
            .ToList()
            .AsReadOnly();
}
