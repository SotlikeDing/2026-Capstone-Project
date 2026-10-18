using Microsoft.Data.SqlClient;
using MobileApi.Models;
using MobileApi.Models.Auth;
using MobileApi.Services;

namespace MobileApi.Database;

public class Auth_db
{
    // The app gives up on a request after 20 s, and SQL's default 30 s command timeout meant a
    // blocked query outlived the request it served (seen 13 Oct 2026: a refresh stuck behind a
    // lock on users). A request retries once at most, so it fails within about 6 + 0.5 + 6 s.
    private const int CommandTimeoutSeconds = 6;
    private const int ConnectTimeoutSeconds = 5;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(500);

    private readonly string _connectionString;
    // Scoped per request: only the first transient failure of a request is retried.
    private bool _retryUsed;

    public Auth_db(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' was not found.");
        _connectionString = new SqlConnectionStringBuilder(connectionString)
        {
            CommandTimeout = CommandTimeoutSeconds,
            ConnectTimeout = ConnectTimeoutSeconds
        }.ConnectionString;
    }

    /// <summary>
    /// SQL errors that a second try can get past: timeouts (also behind a lock), deadlocks,
    /// lock timeouts and dropped or unreachable connections. AuthController answers these with 503.
    /// </summary>
    public static bool IsTransient(SqlException exception) => exception.Number is
        -2 // command timeout
        or 1205 // deadlock victim
        or 1222 // lock request timeout
        or 233 or 64 or 10053 or 10054 or 10060 or 11001 // connection lost / unreachable
        or 4060 or 40197 or 40501 or 40613 or 49918 or 49919 or 49920; // server busy / unavailable

    private async Task<T> WithRetryAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            return await operation();
        }
        catch (SqlException exception) when (!_retryUsed && IsTransient(exception))
        {
            _retryUsed = true;
            Console.WriteLine($"[Auth_db] Transient SQL error {exception.Number}, retrying once: {exception.Message}");
            await Task.Delay(RetryDelay);
            return await operation();
        }
    }

    private Task WithRetryAsync(Func<Task> operation) =>
        WithRetryAsync(async () => { await operation(); return true; });

    /// <summary>
    /// The user for this email when the password matches, else null. The caller still has
    /// to refuse unverified or suspended accounts (see IsEmailVerified / IsSuspended).
    /// </summary>
    public Task<AuthenticatedUser?> ValidateUserCredentialsAsync(string email, string password) =>
        WithRetryAsync(() => ValidateUserCredentialsOnceAsync(email, password));

    private async Task<AuthenticatedUser?> ValidateUserCredentialsOnceAsync(
        string email,
        string password)
    {
        const string sql = """
            SELECT TOP 1
                u.userId,
                u.uEmail,
                u.uName,
                u.uSurname,
                u.uPassword,
                CAST(ISNULL(u.uIsEmailVerified, 0) AS BIT) AS isEmailVerified,
                CAST(ISNULL(u.uIsSuspended, 0) AS BIT) AS isSuspended
            FROM users u
            WHERE u.uEmail = @Email;
            """;

        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand(sql, connection);

        command.Parameters.AddWithValue("@Email", email.Trim());

        await connection.OpenAsync();
        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return null;
        }

        var stored = reader["uPassword"]?.ToString();
        var (valid, needsRehash) = PasswordHasher.Verify(password, stored);
        if (!valid)
        {
            return null;
        }

        var user = new AuthenticatedUser
        {
            UserId = reader.GetInt32(reader.GetOrdinal("userId")),
            Email = reader["uEmail"]?.ToString() ?? string.Empty,
            Name = reader["uName"]?.ToString() ?? string.Empty,
            Surname = reader["uSurname"]?.ToString() ?? string.Empty,
            IsEmailVerified = reader.GetBoolean(reader.GetOrdinal("isEmailVerified")),
            IsSuspended = reader.GetBoolean(reader.GetOrdinal("isSuspended"))
        };
        await reader.CloseAsync();
        // Old unsalted SHA-256 / plain rows are upgraded the first time the right password
        // is used - from the app as well as the website. Best effort: never blocks login.
        if (needsRehash)
        {
            try
            {
                await using var upd = new SqlCommand("UPDATE users SET uPassword = @p WHERE userId = @id", connection);
                upd.Parameters.AddWithValue("@p", PasswordHasher.Hash(password));
                upd.Parameters.AddWithValue("@id", user.UserId);
                await upd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Auth_db] Could not upgrade password hash for user {user.UserId}: {ex.Message}");
            }
        }

        user.AvailableRoles = await GetRolesAsync(connection, user.UserId);
        SelectRole(user);
        return user;
    }

    /// <summary>
    /// Whether the password matches users.uPassword - the same rule as the WebAPI (see
    /// PasswordHasher: salted PBKDF2, or a legacy SHA-256/plain value), since both log in
    /// against the same table.
    /// </summary>
    public static bool PasswordMatches(string password, string? stored) => PasswordHasher.Verify(password, stored).Valid;

    public Task<AuthenticatedUser?> GetAuthenticatedUserByIdAsync(int userId) =>
        WithRetryAsync(() => GetAuthenticatedUserByIdOnceAsync(userId));

    private async Task<AuthenticatedUser?> GetAuthenticatedUserByIdOnceAsync(int userId)
    {
        const string sql = """
            SELECT TOP 1
                u.userId,
                u.uEmail,
                u.uName,
                u.uSurname,
                CAST(ISNULL(u.uIsEmailVerified, 0) AS BIT) AS isEmailVerified,
                CAST(ISNULL(u.uIsSuspended, 0) AS BIT) AS isSuspended
            FROM users u
            WHERE u.userId = @UserId;
            """;

        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand(sql, connection);

        command.Parameters.AddWithValue("@UserId", userId);

        await connection.OpenAsync();
        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return null;
        }

        var user = new AuthenticatedUser
        {
            UserId = reader.GetInt32(reader.GetOrdinal("userId")),
            Email = reader["uEmail"]?.ToString() ?? string.Empty,
            Name = reader["uName"]?.ToString() ?? string.Empty,
            Surname = reader["uSurname"]?.ToString() ?? string.Empty,
            IsEmailVerified = reader.GetBoolean(reader.GetOrdinal("isEmailVerified")),
            IsSuspended = reader.GetBoolean(reader.GetOrdinal("isSuspended"))
        };
        await reader.CloseAsync();
        user.AvailableRoles = await GetRolesAsync(connection, user.UserId);
        SelectRole(user);
        return user.UserRoleId > 0 ? user : null;
    }

    private static async Task<List<AuthenticatedRole>> GetRolesAsync(SqlConnection connection, int userId)
    {
        const string sql = """
            SELECT ur.userRoleId, ISNULL(r.roleCode, ''), ISNULL(r.roleName, '')
            FROM dbo.userRoles ur
            INNER JOIN dbo.roles r ON r.roleId = ur.urRoleId
            WHERE ur.urUserId = @UserId
            ORDER BY ur.userRoleId;
            """;
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@UserId", userId);
        await using var reader = await command.ExecuteReaderAsync();
        var roles = new List<AuthenticatedRole>();
        while (await reader.ReadAsync())
        {
            var roleCode = reader.GetString(1).Trim();
            var normalized = new string(roleCode.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
            roles.Add(new AuthenticatedRole
            {
                UserRoleId = reader.GetInt32(0),
                RoleCode = roleCode,
                RoleName = reader.GetString(2),
                IsSupportedMobileRole = normalized is "HUNTER" or "JAGTER" or "PH" or "PROFESSIONALHUNTER" or "PROFESSIONELEJAGTER" or "VG" or "VIDEOGRAPHER" or "VIDEOGRAAF"
            });
        }
        return roles;
    }

    /// <summary>
    /// The mobile role always comes from the database - the user never picks one. A
    /// multi-role account gets its most capable mobile role (Hunter, then PH, then VG,
    /// then anything else), and the oldest userRoles row when a role is listed twice.
    /// </summary>
    public static void SelectRole(AuthenticatedUser user)
    {
        var selected = user.AvailableRoles
            .OrderBy(role => MobileRolePriority(role.RoleCode))
            .ThenBy(role => role.UserRoleId)
            .FirstOrDefault();

        if (selected is null)
        {
            return;
        }

        user.UserRoleId = selected.UserRoleId;
        user.RoleCode = selected.RoleCode;
        user.RoleName = selected.RoleName;
    }

    private static int MobileRolePriority(string roleCode) => MobileAccessService.ParseRole(roleCode) switch
    {
        MobileRole.Hunter => 0,
        MobileRole.Ph => 1,
        MobileRole.Vg => 2,
        _ => 3
    };

    public Task StoreRefreshTokenAsync(int userId, string refreshToken, DateTime expiresAtUtc, string? ipAddress) =>
        WithRetryAsync(() => StoreRefreshTokenOnceAsync(userId, refreshToken, expiresAtUtc, ipAddress));

    private async Task StoreRefreshTokenOnceAsync(int userId, string refreshToken, DateTime expiresAtUtc, string? ipAddress)
    {
        const string sql = """
            INSERT INTO AuthTokens (atToken, atCreated, atExpired, atUserId, atIP)
            VALUES (@Token, SYSUTCDATETIME(), @ExpiresAtUtc, @UserId, @IpAddress);
            """;

        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand(sql, connection);

        command.Parameters.AddWithValue("@Token", refreshToken);
        command.Parameters.AddWithValue("@ExpiresAtUtc", expiresAtUtc);
        command.Parameters.AddWithValue("@UserId", userId);
        command.Parameters.AddWithValue("@IpAddress", (object?)ipAddress ?? DBNull.Value);

        await connection.OpenAsync();
        await command.ExecuteNonQueryAsync();
    }

    public Task<StoredRefreshToken?> GetRefreshTokenAsync(string refreshToken) =>
        WithRetryAsync(() => GetRefreshTokenOnceAsync(refreshToken));

    private async Task<StoredRefreshToken?> GetRefreshTokenOnceAsync(string refreshToken)
    {
        const string sql = """
            SELECT TOP 1 atToken, atUserId, atExpired, atIP
            FROM AuthTokens
            WHERE atToken = @Token
              AND atExpired > SYSUTCDATETIME();
            """;

        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand(sql, connection);

        command.Parameters.AddWithValue("@Token", refreshToken);

        await connection.OpenAsync();
        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new StoredRefreshToken
        {
            Token = reader["atToken"]?.ToString() ?? string.Empty,
            UserId = reader.GetInt32(reader.GetOrdinal("atUserId")),
            ExpiresAtUtc = reader.GetDateTime(reader.GetOrdinal("atExpired")),
            IpAddress = reader["atIP"]?.ToString()
        };
    }

    public Task DeleteRefreshTokenAsync(string refreshToken) =>
        WithRetryAsync(() => DeleteRefreshTokenOnceAsync(refreshToken));

    private async Task DeleteRefreshTokenOnceAsync(string refreshToken)
    {
        const string sql = "DELETE FROM AuthTokens WHERE atToken = @Token;";

        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand(sql, connection);

        command.Parameters.AddWithValue("@Token", refreshToken);

        await connection.OpenAsync();
        await command.ExecuteNonQueryAsync();
    }

    public Task DeleteRefreshTokensForUserAsync(int userId) =>
        WithRetryAsync(() => DeleteRefreshTokensForUserOnceAsync(userId));

    private async Task DeleteRefreshTokensForUserOnceAsync(int userId)
    {
        const string sql = "DELETE FROM AuthTokens WHERE atUserId = @UserId;";

        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand(sql, connection);

        command.Parameters.AddWithValue("@UserId", userId);

        await connection.OpenAsync();
        await command.ExecuteNonQueryAsync();
    }
}
