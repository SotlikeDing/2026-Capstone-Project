using Microsoft.Data.SqlClient;
using MobileApi.Models;

namespace MobileApi.Database;

public class Users_db : IUserProfilePreferencesRepository
{
    private readonly string _connectionString;

    public Users_db(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<AppUser?> GetByEmailAsync(string email)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        const string query = """
                             SELECT TOP 1 userId, uName, uSurname, uEmail
                             FROM users
                             WHERE uEmail = @email
                             """;

        using var cmd = new SqlCommand(query, conn);
        cmd.Parameters.AddWithValue("@email", email);

        using var reader = await cmd.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new AppUser
        {
            Id = reader.GetInt32(reader.GetOrdinal("userId")),
            Name = reader.GetString(reader.GetOrdinal("uName")),
            Surname = reader.GetString(reader.GetOrdinal("uSurname")),
            Email = reader.GetString(reader.GetOrdinal("uEmail"))
        };
    }

    public async Task<bool?> GetRowlandWardEnabledAsync(int userId)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        const string query = """
                             SELECT CAST(ISNULL(uRowlandWardEnabled, 0) AS BIT)
                             FROM users
                             WHERE userId = @userId
                             """;

        using var cmd = new SqlCommand(query, conn);
        cmd.Parameters.AddWithValue("@userId", userId);

        var result = await cmd.ExecuteScalarAsync();
        return result is null or DBNull ? null : Convert.ToBoolean(result);
    }

    public async Task<bool> UpdateRowlandWardEnabledAsync(int userId, bool enabled)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        const string query = """
                             UPDATE users
                             SET uRowlandWardEnabled = @enabled
                             WHERE userId = @userId
                             """;

        using var cmd = new SqlCommand(query, conn);
        cmd.Parameters.AddWithValue("@userId", userId);
        cmd.Parameters.AddWithValue("@enabled", enabled);

        return await cmd.ExecuteNonQueryAsync() == 1;
    }
}

