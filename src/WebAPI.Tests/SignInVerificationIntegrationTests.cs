using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WebAPI.Database;
using WebAPI.Models;
using Xunit;

namespace WebAPI.Tests;

/// <summary>
/// User_db.AuthenticateUser against a real SQL Server: it used to overwrite the stored
/// IsEmailVerified with true once the password matched, so SignIn's "verify your email
/// first" gate never fired. Proves the stored value now comes through. Self-skips
/// without a reachable LocalDB and removes the user it creates.
/// </summary>
[Collection(LocalDbCollection.Name)]
public class SignInVerificationIntegrationTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private const string Password = "Verify!Passw0rd#2026";
    private readonly string _connectionString;
    private readonly string _email = $"verify.{Guid.NewGuid():N}@signin.test";
    private int _userId;
    private bool _dbAvailable;

    public SignInVerificationIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _connectionString = factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection")!;
    }

    public async Task InitializeAsync()
    {
        try
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            using var cmd = new SqlCommand(@"
                INSERT INTO users (uName, uSurname, uEmail, uPassword, uPhone, uIsEmailVerified, uIsPhoneVerified, uRegistrationDate, uIsSuspended)
                OUTPUT inserted.userId
                VALUES ('Verify', 'Test', @e, @p, '0820000097', 0, 0, SYSUTCDATETIME(), 0)", conn);
            cmd.Parameters.AddWithValue("@e", _email);
            cmd.Parameters.AddWithValue("@p", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Password))).ToLowerInvariant());
            _userId = (int)(await cmd.ExecuteScalarAsync())!;
            _dbAvailable = true;
        }
        catch
        {
            _dbAvailable = false;
        }
    }

    public async Task DisposeAsync()
    {
        if (!_dbAvailable) return;
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand("DELETE FROM users WHERE userId = @id", conn);
        cmd.Parameters.AddWithValue("@id", _userId);
        await cmd.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task AuthenticateUser_reports_the_stored_verification_state()
    {
        if (!_dbAvailable) return;
        var db = new User_db(_connectionString);

        var before = await db.AuthenticateUser(new LoginRequest { Email = _email, Password = Password });
        Assert.NotNull(before);
        Assert.False(before!.IsEmailVerified);

        Assert.True(await db.VerifyEmail(_email));
        var after = await db.AuthenticateUser(new LoginRequest { Email = _email, Password = Password });
        Assert.True(after!.IsEmailVerified);

        Assert.Null(await db.AuthenticateUser(new LoginRequest { Email = _email, Password = "wrong" }));
    }

    [Fact]
    public async Task An_old_sha256_password_is_upgraded_to_salted_pbkdf2_on_login()
    {
        if (!_dbAvailable) return;
        var db = new User_db(_connectionString);

        Assert.NotNull(await db.AuthenticateUser(new LoginRequest { Email = _email, Password = Password }));

        string stored;
        using (var conn = new SqlConnection(_connectionString))
        {
            await conn.OpenAsync();
            using var cmd = new SqlCommand("SELECT uPassword FROM users WHERE userId = @id", conn);
            cmd.Parameters.AddWithValue("@id", _userId);
            stored = (string)(await cmd.ExecuteScalarAsync())!;
        }
        Assert.StartsWith("pbkdf2-sha256$", stored);
        Assert.NotNull(await db.AuthenticateUser(new LoginRequest { Email = _email, Password = Password })); // still works
        Assert.Null(await db.AuthenticateUser(new LoginRequest { Email = _email, Password = "wrong" }));
    }
}
