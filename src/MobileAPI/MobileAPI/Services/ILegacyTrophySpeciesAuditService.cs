using MobileApi.Models.RowlandWard;

namespace MobileApi.Services;

public interface ILegacyTrophySpeciesAuditService
{
    Task<LegacyTrophySpeciesAuditResponse> AuditAsync();
}
