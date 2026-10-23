using System.Security.Cryptography;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;

namespace Algara.Identity.Services;

// Keep the established Algara password format for new and existing accounts.
public static class AccountPasswordCodec
{
    public static (string Hash, string Salt) Create(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = KeyDerivation.Pbkdf2(password, salt, KeyDerivationPrf.HMACSHA256, 10000, 32);
        return (Convert.ToBase64String(hash), Convert.ToBase64String(salt));
    }
}
