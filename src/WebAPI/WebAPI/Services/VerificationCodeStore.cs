using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;

namespace WebAPI.Services;

public enum VerificationCodeResult { Valid, Invalid, Expired, Locked, NotFound }

/// <summary>
/// Issues and checks the 6-digit e-mail verification codes. Codes are drawn from a
/// cryptographic RNG, compared as hashes, expire after 24 hours and stop working
/// after <see cref="MaxFailedAttempts"/> wrong guesses (a new code must be requested),
/// so a code can't be brute-forced.
/// </summary>
public interface IVerificationCodeStore
{
    const int MaxFailedAttempts = 5;
    Task<string> IssueAsync(string email);
    Task<VerificationCodeResult> CheckAsync(string email, string code);
    Task RemoveAsync(string email);
}

internal static class VerificationCodes
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);
    public static string NewCode() => RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
    public static string Key(string email) => email.Trim().ToLowerInvariant();
    public static byte[] Hash(string email, string code) => SHA256.HashData(Encoding.UTF8.GetBytes(Key(email) + ":" + code.Trim()));
}

/// <summary>SQL-backed store (dbo.emailVerificationCodes) - survives deploys and is shared by every instance.</summary>
public sealed class SqlVerificationCodeStore : IVerificationCodeStore
{
    private readonly string _connectionString;
    public SqlVerificationCodeStore(string connectionString) => _connectionString = connectionString;

    public async Task<string> IssueAsync(string email)
    {
        var code = VerificationCodes.NewCode();
        const string q = @"MERGE dbo.emailVerificationCodes WITH (HOLDLOCK) AS t
                           USING (SELECT @email AS email) AS s ON t.email = s.email
                           WHEN MATCHED THEN UPDATE SET codeHash = @hash, expiresAt = @expires, failedAttempts = 0, createdAt = SYSUTCDATETIME()
                           WHEN NOT MATCHED THEN INSERT (email, codeHash, expiresAt) VALUES (@email, @hash, @expires);";
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@email", VerificationCodes.Key(email));
        cmd.Parameters.AddWithValue("@hash", VerificationCodes.Hash(email, code));
        cmd.Parameters.AddWithValue("@expires", DateTime.UtcNow.Add(VerificationCodes.Lifetime));
        await cmd.ExecuteNonQueryAsync();
        return code;
    }

    public async Task<VerificationCodeResult> CheckAsync(string email, string code)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        // One atomic statement: read the row and, on a wrong code, count the attempt.
        const string q = @"DECLARE @hit BIT, @expired BIT, @locked BIT;
                           SELECT @hit = CASE WHEN codeHash = @hash THEN 1 ELSE 0 END,
                                  @expired = CASE WHEN expiresAt < SYSUTCDATETIME() THEN 1 ELSE 0 END,
                                  @locked = CASE WHEN failedAttempts >= @max THEN 1 ELSE 0 END
                           FROM dbo.emailVerificationCodes WITH (UPDLOCK, HOLDLOCK) WHERE email = @email;
                           IF @hit IS NOT NULL AND @hit = 0 AND @locked = 0
                               UPDATE dbo.emailVerificationCodes SET failedAttempts = failedAttempts + 1 WHERE email = @email;
                           SELECT CASE WHEN @hit IS NULL THEN 'NotFound' WHEN @locked = 1 THEN 'Locked' WHEN @expired = 1 THEN 'Expired'
                                       WHEN @hit = 1 THEN 'Valid' ELSE 'Invalid' END;";
        await using var cmd = new SqlCommand(q, conn);
        cmd.Parameters.AddWithValue("@email", VerificationCodes.Key(email));
        cmd.Parameters.AddWithValue("@hash", VerificationCodes.Hash(email, code));
        cmd.Parameters.AddWithValue("@max", IVerificationCodeStore.MaxFailedAttempts);
        var result = (string?)await cmd.ExecuteScalarAsync();
        return Enum.TryParse<VerificationCodeResult>(result, out var parsed) ? parsed : VerificationCodeResult.NotFound;
    }

    public async Task RemoveAsync(string email)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand("DELETE FROM dbo.emailVerificationCodes WHERE email = @email", conn);
        cmd.Parameters.AddWithValue("@email", VerificationCodes.Key(email));
        await cmd.ExecuteNonQueryAsync();
    }
}

/// <summary>Process-local fallback (unit tests / no database); same rules as the SQL store.</summary>
public sealed class InMemoryVerificationCodeStore : IVerificationCodeStore
{
    private readonly ConcurrentDictionary<string, (byte[] Hash, DateTime Expires, int Failed)> _codes = new();

    public Task<string> IssueAsync(string email)
    {
        var code = VerificationCodes.NewCode();
        _codes[VerificationCodes.Key(email)] = (VerificationCodes.Hash(email, code), DateTime.UtcNow.Add(VerificationCodes.Lifetime), 0);
        return Task.FromResult(code);
    }

    public Task<VerificationCodeResult> CheckAsync(string email, string code)
    {
        var key = VerificationCodes.Key(email);
        if (!_codes.TryGetValue(key, out var entry)) return Task.FromResult(VerificationCodeResult.NotFound);
        if (entry.Failed >= IVerificationCodeStore.MaxFailedAttempts) return Task.FromResult(VerificationCodeResult.Locked);
        if (entry.Expires < DateTime.UtcNow) return Task.FromResult(VerificationCodeResult.Expired);
        if (CryptographicOperations.FixedTimeEquals(entry.Hash, VerificationCodes.Hash(email, code)))
            return Task.FromResult(VerificationCodeResult.Valid);
        _codes[key] = entry with { Failed = entry.Failed + 1 };
        return Task.FromResult(VerificationCodeResult.Invalid);
    }

    public Task RemoveAsync(string email)
    {
        _codes.TryRemove(VerificationCodes.Key(email), out _);
        return Task.CompletedTask;
    }
}
