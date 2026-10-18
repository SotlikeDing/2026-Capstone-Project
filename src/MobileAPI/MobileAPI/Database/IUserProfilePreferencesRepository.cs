namespace MobileApi.Database;

public interface IUserProfilePreferencesRepository
{
    Task<bool?> GetRowlandWardEnabledAsync(int userId);
    Task<bool> UpdateRowlandWardEnabledAsync(int userId, bool enabled);
}
