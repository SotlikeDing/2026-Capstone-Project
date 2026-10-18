using MobileApi.Models.RowlandWard;

namespace MobileApi.Database;

public interface ILegacyTrophyAuditRepository
{
    Task<IReadOnlyList<LegacyTrophySpeciesValue>> GetDistinctLegacySpeciesAsync();
}
