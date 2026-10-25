using System.Security.Cryptography;
using System.Text.Json;
using Algara.Identity.Data;
using Algara.Identity.Models;

namespace Algara.Web.Services;

/// <summary>Optimistic versions only; every mutation also validates the current actor and ownership.</summary>
public static class CustomerManagementVersions
{
    public static string Account(ApplicationUser user) => Hash(new
    {
        user.N, user.Id, user.UserName, user.Email, user.FullName, user.DisplayName,
        user.FirstName, user.LastName, user.PhoneNumber, user.IsActive, user.AccessSuspendedAtUtc,
        user.FailedLoginAttempts, user.LockoutUntil, user.SecurityStamp,
        user.RequireEmailConfirmation, user.EmailConfirmed
    });

    public static string AddressBook(IEnumerable<UserAddress> addresses) => Hash(addresses.OrderBy(a => a.N).Select(a => new
    {
        a.N, a.UserN, a.FirstName, a.LastName, a.PhoneNumber, a.Email, a.AddressLine1, a.AddressLine2,
        a.City, a.PostalCode, a.Country, a.IsDefault, a.CreatedAt, a.UpdatedAt
    }).ToArray());

    public static string Session(UserSession session) => Hash(new
    {
        session.Id, session.UserN, session.CreatedAt, session.DeviceInfo, session.IsActive
    });

    public static bool IsVersion(string? value)
    {
        if (value?.Length != 44) return false;
        Span<byte> bytes = stackalloc byte[32];
        return Convert.TryFromBase64String(value, bytes, out var length) && length == 32 &&
            Convert.ToBase64String(bytes) == value;
    }

    private static string Hash(object value) => Convert.ToBase64String(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
}
