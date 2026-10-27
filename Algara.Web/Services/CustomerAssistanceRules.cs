using System.Security.Cryptography;
using System.Text.Json;
using Algara.Data.Models;
using Algara.Identity.Authorization;
using Algara.Identity.Models;

namespace Algara.Web.Services;

public static class CustomerAssistanceRules
{
    public static readonly TimeSpan Duration = TimeSpan.FromMinutes(30);

    public static bool IsMode(CustomerAssistanceMode mode) => mode is CustomerAssistanceMode.Preview or CustomerAssistanceMode.Assist;

    public static bool CanTarget(ApplicationUser user, IEnumerable<string> targetRoles, int actorN, IEnumerable<string> actorRoles) =>
        user.N != actorN && user.IsActive && user.AccessSuspendedAtUtc == null &&
        StaffAccountManagementRules.CanManage(actorRoles, targetRoles);

    public static bool IsActive(CustomerAssistanceSession session, DateTime now) =>
        session.EndedAtUtc == null && session.StartedAtUtc <= now && now < session.ExpiresAtUtc &&
        session.ExpiresAtUtc - session.StartedAtUtc == Duration && IsMode((CustomerAssistanceMode)session.Mode);

    // Contact edits do not invalidate the context. Access, credential and target-role changes do.
    // JSON preserves null versus empty stamps; role ordering is not a security change.
    public static string SecurityVersion(ApplicationUser user, IEnumerable<string> targetRoles) => Convert.ToBase64String(SHA256.HashData(
        JsonSerializer.SerializeToUtf8Bytes(new
        {
            user.N, user.Id, user.UserName, user.Email, user.SecurityStamp, user.IsActive,
            user.AccessSuspendedAtUtc, user.RequireEmailConfirmation, user.EmailConfirmed,
            Roles = targetRoles.Distinct(StringComparer.Ordinal).OrderBy(role => role, StringComparer.Ordinal).ToArray()
        })));
}
