using System.Net.Mail;
using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;

namespace Algara.Web.Services.Email;

public sealed class IssuedAccountEmailToken
{
    public Guid Id { get; init; }
    public string Hash { get; init; } = string.Empty;
    public string LinkValue { get; init; } = string.Empty;
}

public static class AccountEmailRules
{
    public const string InvalidLink = "Връзката е невалидна, изтекла или вече използвана. Поискайте нова връзка.";
    public static string? NormalizeEmail(string? email)
    {
        email = email?.Trim();
        return email?.Length is > 0 and <= 256 && !email.Any(char.IsControl) &&
            MailAddress.TryCreate(email, out var address) && address.Address == email
            ? email.ToLowerInvariant() : null;
    }

    public static IssuedAccountEmailToken Issue()
    {
        var id = Guid.NewGuid();
        var secret = RandomNumberGenerator.GetBytes(32);
        return new() { Id = id, Hash = Convert.ToHexString(SHA256.HashData(secret)),
            LinkValue = $"{id:N}.{WebEncoders.Base64UrlEncode(secret)}" };
    }

    public static bool TryParse(string? value, out Guid id, out byte[] secret)
    {
        id = Guid.Empty; secret = [];
        if (value?.Length != 76 || value[32] != '.' || !Guid.TryParseExact(value[..32], "N", out id) || id.ToString("N") != value[..32]) return false;
        try
        {
            secret = WebEncoders.Base64UrlDecode(value[33..]);
            return secret.Length == 32 && WebEncoders.Base64UrlEncode(secret) == value[33..];
        }
        catch (FormatException) { return false; }
    }

    public static bool MatchesHash(byte[] secret, string hash)
    {
        if (secret.Length != 32 || hash.Length != 64) return false;
        try { return CryptographicOperations.FixedTimeEquals(SHA256.HashData(secret), Convert.FromHexString(hash)); }
        catch (FormatException) { return false; }
    }

    public static bool ValidPassword(string? password) => password?.Length is >= 6 and <= 1024;
}
