using System.Globalization;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using MobileApi.Models.RowlandWard;

namespace MobileApi.Services;

public sealed class RowlandWardSpeciesResolver : IRowlandWardSpeciesResolver
{
    public const string Exact = "EXACT";
    public const string Alias = "ALIAS";
    public const string HighConfidenceFuzzy = "HIGH_CONFIDENCE_FUZZY";
    public const string Ambiguous = "AMBIGUOUS";
    public const string NoMatch = "NO_MATCH";

    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);
    private readonly IRowlandWardCatalogueService _catalogue;
    private readonly IMemoryCache _cache;

    public RowlandWardSpeciesResolver(
        IRowlandWardCatalogueService catalogue,
        IMemoryCache cache)
    {
        _catalogue = catalogue;
        _cache = cache;
    }

    public IReadOnlyList<RowlandWardSpeciesSuggestion> Search(
        string query,
        string? categoryId,
        int limit)
    {
        var normalized = Normalize(query);
        if (normalized.Length == 0)
        {
            return [];
        }

        var safeLimit = Math.Clamp(limit, 1, 50);
        var normalizedCategory = string.IsNullOrWhiteSpace(categoryId) ? null : categoryId.Trim();
        var key = $"rw:search:{normalizedCategory ?? "*"}:{safeLimit}:{normalized}";

        return _cache.GetOrCreate(key, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            return RankCandidates(normalized, normalizedCategory, includeWeakFuzzy: false)
                .Take(safeLimit)
                .Select(candidate => ToSuggestion(candidate.RecordClass, candidate.Confidence))
                .ToList()
                .AsReadOnly();
        })!;
    }

    public RowlandWardSpeciesMatchResult Match(string input, int limit)
    {
        var normalized = Normalize(input);
        if (normalized.Length == 0)
        {
            return Result(NoMatch, normalized, null, [], false);
        }

        var safeLimit = Math.Clamp(limit, 1, 20);
        var key = $"rw:match:{safeLimit}:{normalized}";
        return _cache.GetOrCreate(key, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            return MatchCore(normalized, safeLimit);
        })!;
    }

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Normalize(NormalizationForm.FormKC).Trim();
        var builder = new StringBuilder(normalized.Length);
        var previousWasWhitespace = false;

        foreach (var character in normalized)
        {
            if (char.IsWhiteSpace(character))
            {
                if (!previousWasWhitespace && builder.Length > 0)
                {
                    builder.Append(' ');
                }
                previousWasWhitespace = true;
                continue;
            }

            builder.Append(char.ToLower(character, CultureInfo.InvariantCulture));
            previousWasWhitespace = false;
        }

        return builder.ToString();
    }

    public static int DamerauLevenshteinDistance(string first, string second)
    {
        var rows = first.Length + 1;
        var columns = second.Length + 1;
        var matrix = new int[rows, columns];

        for (var row = 0; row < rows; row++)
        {
            matrix[row, 0] = row;
        }
        for (var column = 0; column < columns; column++)
        {
            matrix[0, column] = column;
        }

        for (var row = 1; row < rows; row++)
        {
            for (var column = 1; column < columns; column++)
            {
                var substitutionCost = first[row - 1] == second[column - 1] ? 0 : 1;
                matrix[row, column] = Math.Min(
                    Math.Min(
                        matrix[row - 1, column] + 1,
                        matrix[row, column - 1] + 1),
                    matrix[row - 1, column - 1] + substitutionCost);

                if (row > 1
                    && column > 1
                    && first[row - 1] == second[column - 2]
                    && first[row - 2] == second[column - 1])
                {
                    matrix[row, column] = Math.Min(
                        matrix[row, column],
                        matrix[row - 2, column - 2] + substitutionCost);
                }
            }
        }

        return matrix[first.Length, second.Length];
    }

    private RowlandWardSpeciesMatchResult MatchCore(string normalized, int limit)
    {
        var exactMatches = _catalogue.RecordClasses
            .Where(record => Normalize(record.DisplayNameAf) == normalized)
            .ToList();
        if (exactMatches.Count == 1)
        {
            return Result(Exact, normalized, exactMatches[0], [], false);
        }
        if (exactMatches.Count > 1)
        {
            return AmbiguousResult(normalized, exactMatches, limit, 100);
        }

        var aliasMatches = _catalogue.RecordClasses
            .Where(record => record.Aliases.Any(alias => Normalize(alias) == normalized))
            .ToList();
        if (aliasMatches.Count == 1)
        {
            return Result(Alias, normalized, aliasMatches[0], [], false);
        }
        if (aliasMatches.Count > 1)
        {
            return AmbiguousResult(normalized, aliasMatches, limit, 100);
        }

        var candidates = RankCandidates(normalized, categoryId: null, includeWeakFuzzy: true)
            .Take(limit)
            .ToList();
        if (candidates.Count == 0)
        {
            return Result(NoMatch, normalized, null, [], false);
        }

        var top = candidates[0];
        var closeCandidates = candidates
            .Where(candidate => candidate.Confidence >= top.Confidence - 4)
            .ToList();

        if (top.Confidence >= 84 && closeCandidates.Count == 1)
        {
            return Result(
                HighConfidenceFuzzy,
                normalized,
                top.RecordClass,
                [ToSuggestion(top.RecordClass, top.Confidence)],
                false);
        }

        if (top.Confidence >= 70 && closeCandidates.Count > 1)
        {
            return Result(
                Ambiguous,
                normalized,
                null,
                closeCandidates.Select(candidate => ToSuggestion(candidate.RecordClass, candidate.Confidence)).ToList(),
                true);
        }

        var suggestions = candidates
            .Where(candidate => candidate.Confidence >= 65)
            .Select(candidate => ToSuggestion(candidate.RecordClass, candidate.Confidence))
            .ToList();
        return Result(NoMatch, normalized, null, suggestions, suggestions.Count > 1);
    }

    private IReadOnlyList<RankedCandidate> RankCandidates(
        string normalized,
        string? categoryId,
        bool includeWeakFuzzy)
    {
        var candidates = new List<RankedCandidate>();
        foreach (var record in _catalogue.RecordClasses)
        {
            if (categoryId is not null && record.CategoryId != categoryId)
            {
                continue;
            }

            var display = Normalize(record.DisplayNameAf);
            var aliases = record.Aliases.Select(Normalize).Distinct(StringComparer.Ordinal).ToList();
            var confidence = BestSearchConfidence(normalized, display, aliases, includeWeakFuzzy);
            if (confidence > 0)
            {
                candidates.Add(new RankedCandidate(record, confidence));
            }
        }

        return candidates
            .OrderByDescending(candidate => candidate.Confidence)
            .ThenBy(candidate => candidate.RecordClass.DisplayNameAf, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.RecordClass.Id, StringComparer.Ordinal)
            .ToList()
            .AsReadOnly();
    }

    private static int BestSearchConfidence(
        string query,
        string display,
        IReadOnlyList<string> aliases,
        bool includeWeakFuzzy)
    {
        if (display == query)
        {
            return 100;
        }
        if (display.StartsWith(query, StringComparison.Ordinal))
        {
            return 97;
        }
        if (display.Split(' ').Any(word => word.StartsWith(query, StringComparison.Ordinal)))
        {
            return 95;
        }
        if (aliases.Any(alias => alias == query))
        {
            return 94;
        }
        if (aliases.Any(alias => alias.StartsWith(query, StringComparison.Ordinal)))
        {
            return 92;
        }
        if (display.Contains(query, StringComparison.Ordinal)
            || aliases.Any(alias => alias.Contains(query, StringComparison.Ordinal)))
        {
            return 88;
        }

        var confidence = new[] { display }.Concat(aliases)
            .Select(candidate => Similarity(query, candidate))
            .DefaultIfEmpty(0)
            .Max();
        var threshold = includeWeakFuzzy ? 55 : 70;
        return confidence >= threshold ? confidence : 0;
    }

    private static int Similarity(string first, string second)
    {
        var longest = Math.Max(first.Length, second.Length);
        if (longest == 0)
        {
            return 100;
        }

        var distance = DamerauLevenshteinDistance(first, second);
        return Math.Max(0, 100 - (distance * 100 / longest));
    }

    private static RowlandWardSpeciesSuggestion ToSuggestion(
        RowlandWardRecordClass record,
        int confidence) => new(
            record.Id,
            record.DisplayNameAf,
            record.LatinName,
            record.CategoryId,
            record.MethodCode,
            confidence);

    private static RowlandWardSpeciesMatchResult AmbiguousResult(
        string normalized,
        IReadOnlyList<RowlandWardRecordClass> matches,
        int limit,
        int confidence) => Result(
            Ambiguous,
            normalized,
            null,
            matches
                .OrderBy(record => record.DisplayNameAf, StringComparer.OrdinalIgnoreCase)
                .ThenBy(record => record.Id, StringComparer.Ordinal)
                .Take(limit)
                .Select(record => ToSuggestion(record, confidence))
                .ToList(),
            true);

    private static RowlandWardSpeciesMatchResult Result(
        string outcome,
        string normalized,
        RowlandWardRecordClass? match,
        IReadOnlyList<RowlandWardSpeciesSuggestion> suggestions,
        bool requiresSelection)
    {
        var messageAf = outcome switch
        {
            Ambiguous => "Kies asseblief die korrekte wildspesie.",
            NoMatch => "Geen goedgekeurde wildspesie stem met jou invoer ooreen nie.",
            _ => null
        };

        return new RowlandWardSpeciesMatchResult(
            outcome,
            normalized,
            match is null ? null : RowlandWardRecordClassSummary.From(match),
            suggestions,
            requiresSelection,
            messageAf);
    }

    private sealed record RankedCandidate(RowlandWardRecordClass RecordClass, int Confidence);
}
