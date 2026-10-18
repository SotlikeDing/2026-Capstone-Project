using MobileApi.Database;

namespace MobileAPI.Tests;

/// <summary>
/// The app logs in against the same users table the WebAPI writes. The WebAPI stores the
/// SHA-256 hex of the password, so the MobileAPI must accept that (it used to compare the
/// typed password with the stored hash, which locked every web-registered user out of the app).
/// </summary>
public class PasswordMatchingTests
{
    /// <summary>The value User_db.HashPassword stores: SHA-256, lower-case hex.</summary>
    private static string Sha256Hex(string s) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(s))).ToLowerInvariant();

    [Fact]
    public void A_web_registered_password_matches_its_stored_hash()
    {
        Assert.True(Auth_db.PasswordMatches("E2e!Passw0rd#2026", Sha256Hex("E2e!Passw0rd#2026")));
    }

    [Fact]
    public void A_salted_pbkdf2_password_matches()
    {
        var stored = MobileApi.Services.PasswordHasher.Hash("E2e!Passw0rd#2026");
        Assert.StartsWith("pbkdf2-sha256$", stored);
        Assert.True(Auth_db.PasswordMatches("E2e!Passw0rd#2026", stored));
        Assert.False(Auth_db.PasswordMatches("wrong", stored));
    }

    [Fact]
    public void A_wrong_password_does_not_match()
    {
        Assert.False(Auth_db.PasswordMatches("wrong", Sha256Hex("E2e!Passw0rd#2026")));
        Assert.False(Auth_db.PasswordMatches("e2e!passw0rd#2026", Sha256Hex("E2e!Passw0rd#2026")));
    }

    [Fact]
    public void Legacy_plain_text_seed_rows_still_match()
    {
        Assert.True(Auth_db.PasswordMatches("Seed123!", "Seed123!"));
    }

    [Theory]
    [InlineData("", "x")]
    [InlineData("x", "")]
    [InlineData("x", null)]
    public void Empty_values_never_match(string password, string? stored)
    {
        Assert.False(Auth_db.PasswordMatches(password, stored));
    }

    [Fact]
    public void The_hash_format_is_lower_case_hex_like_the_WebAPI()
    {
        Assert.Matches("^[0-9a-f]{64}$", Sha256Hex("anything"));
        Assert.False(Auth_db.PasswordMatches("anything", Sha256Hex("anything").ToUpperInvariant()));
    }
}
