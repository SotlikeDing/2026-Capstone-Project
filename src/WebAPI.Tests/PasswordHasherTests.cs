using System.Security.Cryptography;
using System.Text;
using WebAPI.Services;
using Xunit;

namespace WebAPI.Tests;

public class PasswordHasherTests
{
    private const string Password = "Hasher!Passw0rd#2026";

    private static string LegacySha256(string s) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();

    [Fact]
    public void A_new_hash_is_salted_pbkdf2_and_verifies()
    {
        string hash = PasswordHasher.Hash(Password);

        Assert.StartsWith("pbkdf2-sha256$600000$", hash);
        Assert.Equal((true, false), PasswordHasher.Verify(Password, hash));
        Assert.False(PasswordHasher.Verify("wrong", hash).Valid);
    }

    [Fact]
    public void The_same_password_gets_a_different_hash_each_time()
    {
        Assert.NotEqual(PasswordHasher.Hash(Password), PasswordHasher.Hash(Password));
    }

    [Fact]
    public void Old_sha256_and_plain_values_verify_and_ask_to_be_upgraded()
    {
        Assert.Equal((true, true), PasswordHasher.Verify(Password, LegacySha256(Password)));
        Assert.Equal((true, true), PasswordHasher.Verify("Seed123!", "Seed123!"));
        Assert.Equal((false, false), PasswordHasher.Verify("wrong", LegacySha256(Password)));
    }

    [Fact]
    public void A_weaker_pbkdf2_value_still_verifies_but_asks_to_be_upgraded()
    {
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(Password, salt, 1000, HashAlgorithmName.SHA256, 32);
        string stored = $"pbkdf2-sha256$1000${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";

        Assert.Equal((true, true), PasswordHasher.Verify(Password, stored));
    }

    [Theory]
    [InlineData("pbkdf2-sha256$abc$AAAA$AAAA")]
    [InlineData("pbkdf2-sha256$600000$not-base64!$AAAA")]
    [InlineData("pbkdf2-sha256$600000$AAAA")]
    [InlineData("")]
    public void Malformed_or_empty_values_never_verify(string stored)
    {
        Assert.False(PasswordHasher.Verify(Password, stored).Valid);
    }
}
