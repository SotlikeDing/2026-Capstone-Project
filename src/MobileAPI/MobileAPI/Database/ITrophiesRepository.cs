using MobileApi.Models;
using MobileApi.Models.RowlandWard;

namespace MobileApi.Database;

public interface ITrophiesRepository
{
    Task<int> CreateAsync(
        int ownerUserId,
        TrophySaveRequest request,
        RowlandWardEvaluationSnapshot? evaluation = null);

    Task<List<Trophy>> GetForOwnerAsync(int ownerUserId);

    Task<Trophy?> GetByIdForOwnerAsync(int trophyId, int ownerUserId);

    Task<bool> IsPhotoPublicAsync(
        string photoPath,
        CancellationToken cancellationToken = default);

    Task<bool> UpdateForOwnerAsync(
        int trophyId,
        int ownerUserId,
        TrophySaveRequest request,
        RowlandWardEvaluationSnapshot? evaluation = null);

    Task<bool> DeleteForOwnerAsync(int trophyId, int ownerUserId);
}
