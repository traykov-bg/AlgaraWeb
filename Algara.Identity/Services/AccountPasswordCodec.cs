using System.Security.Cryptography;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;

namespace Algara.Identity.Services;

// Keep the established Algara password format for new and existing accounts.
public static class AccountPasswordCodec
{
    public static bool Verify(string? password, string? hash, string? salt)
    {
        if (password?.Length is not (> 0 and <= 1024) || hash == null || salt == null) return false;
        Span<byte> expected = stackalloc byte[32];
        Span<byte> decodedSalt = stackalloc byte[16];
        if (!Convert.TryFromBase64String(hash, expected, out var hashLength) || hashLength != 32 ||
            !Convert.TryFromBase64String(salt, decodedSalt, out var saltLength) || saltLength != 16) return false;
        var actual = KeyDerivation.Pbkdf2(password, decodedSalt.ToArray(), KeyDerivationPrf.HMACSHA256, 10000, 32);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    public static (string Hash, string Salt) Create(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = KeyDerivation.Pbkdf2(password, salt, KeyDerivationPrf.HMACSHA256, 10000, 32);
        return (Convert.ToBase64String(hash), Convert.ToBase64String(salt));
    }
}
