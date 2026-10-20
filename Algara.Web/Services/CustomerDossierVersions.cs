using System.Security.Cryptography;
using System.Text.Json;
using Algara.Data.Models;
using Algara.Identity.Models;

namespace Algara.Web.Services;

/// <summary>Opaque optimistic versions, not authorization credentials.</summary>
public static class CustomerDossierVersions
{
    public static string Account(ApplicationUser user) => Convert.ToBase64String(SHA256.HashData(
        JsonSerializer.SerializeToUtf8Bytes(new
        {
            user.N, user.Id, user.UserName, user.Email, user.DisplayName, user.FirstName,
            user.LastName, user.PhoneNumber, user.IsActive, user.SecurityStamp
        })));

    public static string Link(PartnerAccountLink? link) =>
        link == null ? "none" : Convert.ToBase64String(link.RowVersion);

    public static bool IsAccountVersion(string? value) => IsCanonicalBase64(value, 32);
    public static bool IsRowVersion(string? value) => IsCanonicalBase64(value, 8);
    public static bool IsLinkVersion(string? value) => value == "none" || IsRowVersion(value);

    private static bool IsCanonicalBase64(string? value, int byteCount)
    {
        if (value == null || value.Length != ((byteCount + 2) / 3) * 4) return false;
        Span<byte> bytes = stackalloc byte[32];
        return Convert.TryFromBase64String(value, bytes, out var written) &&
            written == byteCount && Convert.ToBase64String(bytes[..written]) == value;
    }
}
