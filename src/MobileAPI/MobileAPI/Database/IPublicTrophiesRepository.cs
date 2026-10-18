using MobileApi.Models;

namespace MobileApi.Database;

public interface IPublicTrophiesRepository
{
    Task<List<PublicTrophySummary>> GetPublicTrophiesAsync();

    Task<List<string>> GetPublicSpeciesAsync();

    Task<List<string>> GetPublicLegacySpeciesAsync();

    Task<List<string>> GetPublicFirearmTypesAsync();

    Task<PublicTrophyDetail?> GetPublicTrophyByIdAsync(int trophyId);

    Task<List<LeaderboardTrophySummary>> GetLeaderboardAsync(
        IReadOnlyCollection<string>? rowlandWardClassIds,
        bool legacyOnly,
        string? legacySpecies,
        string? firearmType);
}
