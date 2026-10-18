using MobileApi.Models.RowlandWard;

namespace MobileApi.Services;

public interface IRowlandWardSpeciesResolver
{
    IReadOnlyList<RowlandWardSpeciesSuggestion> Search(string query, string? categoryId, int limit);
    RowlandWardSpeciesMatchResult Match(string input, int limit);
}
