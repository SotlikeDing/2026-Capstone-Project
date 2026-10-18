using System.Security.Cryptography;
using System.Text;

namespace MobileApi.Services;

/// <summary>
/// Password storage for users.uPassword: PBKDF2-HMAC-SHA256 with a random 16-byte salt per
/// password and 600 000 iterations (OWASP guidance), stored as
/// "pbkdf2-sha256$iterations$salt$hash" (base64). Older rows hold an unsalted SHA-256 hex
/// value or - legacy seed data - the plain password; those still verify, report
/// NeedsRehash, and the caller replaces them on the next successful login.
/// Identical to the WebAPI's PasswordHasher (both log in against the same table).
/// </summary>
public static class PasswordHasher
{
    private const string Prefix = "pbkdf2-sha256";
    private const int Iterations = 600_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public static string Hash(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return $"{Prefix}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    /// <summary>True when the password matches the stored value; NeedsRehash when that value isn't in the current format/strength.</summary>
    public static (bool Valid, bool NeedsRehash) Verify(string password, string? stored)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(stored)) return (false, false);

        if (stored.StartsWith(Prefix + "$", StringComparison.Ordinal))
        {
            var parts = stored.Split('$');
            if (parts.Length != 4 || !int.TryParse(parts[1], out int iterations) || iterations <= 0) return (false, false);
            try
            {
                byte[] salt = Convert.FromBase64String(parts[2]);
                byte[] expected = Convert.FromBase64String(parts[3]);
                byte[] actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
                bool ok = CryptographicOperations.FixedTimeEquals(actual, expected);
                return (ok, ok && iterations < Iterations);
            }
            catch (FormatException)
            {
                return (false, false);
            }
        }

        // Legacy: unsalted SHA-256 hex (lower-case), or a plain-text seed password.
        byte[] storedBytes = Encoding.UTF8.GetBytes(stored);
        string sha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(password))).ToLowerInvariant();
        bool legacy = CryptographicOperations.FixedTimeEquals(storedBytes, Encoding.UTF8.GetBytes(sha))
                   || CryptographicOperations.FixedTimeEquals(storedBytes, Encoding.UTF8.GetBytes(password));
        return (legacy, legacy);
    }
}
