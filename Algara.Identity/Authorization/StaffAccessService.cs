using System.Security.Claims;
using Algara.Identity.Data;
using Algara.Identity.Models;
using Microsoft.EntityFrameworkCore;

namespace Algara.Identity.Authorization;

/// <summary>A request-scoped snapshot; permissions are never retained in the authentication cookie.</summary>
public sealed class StaffAccessService(IdentityDbContext db)
{
    private readonly Dictionary<(string? UserId, string? Stamp, string? SessionId, bool Authenticated), Task<AccessSnapshot>> _snapshots = new();

    public async Task<bool> IsStaffAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default) =>
        (await GetSnapshotAsync(principal, cancellationToken)).IsStaff;

    public async Task<bool> HasPermissionAsync(ClaimsPrincipal principal, string key, CancellationToken cancellationToken = default)
    {
        var definition = PermissionCatalog.Find(key);
        if (definition == null) return false;
        var snapshot = await GetSnapshotAsync(principal, cancellationToken);
        return snapshot.IsAdmin || (!definition.AdminOnly && snapshot.Permissions.Contains(key));
    }

    private Task<AccessSnapshot> GetSnapshotAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var key = (principal.FindFirstValue(ClaimTypes.NameIdentifier), principal.FindFirstValue("SecurityStamp"),
            principal.FindFirstValue("SessionId"), principal.Identity?.IsAuthenticated == true);
        if (!_snapshots.TryGetValue(key, out var snapshot))
            _snapshots.Add(key, snapshot = LoadSnapshotAsync(principal, cancellationToken));
        return snapshot;
    }

    private async Task<AccessSnapshot> LoadSnapshotAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var user = await FindAuthenticatedUserAsync(db, principal, cancellationToken);
        if (user == null) return new(false, false, new(StringComparer.Ordinal));
        var roles = await db.UserRoles.AsNoTracking().Where(r => r.UserN == user.N)
            .Select(r => new { r.RoleN, r.Role.Name }).ToListAsync(cancellationToken);
        var staff = roles.Where(r => StaffRoles.IsStaff(r.Name)).ToList();
        if (staff.Any(r => r.Name == StaffRoles.Admin)) return new(true, true, new(StringComparer.Ordinal));
        var roleNumbers = staff.Select(r => r.RoleN).ToArray();
        if (roleNumbers.Length == 0) return new(false, false, new(StringComparer.Ordinal));
        var grants = await db.RolePermissions.AsNoTracking().Where(p => roleNumbers.Contains(p.RoleN))
            .Select(p => p.PermissionKey).ToListAsync(cancellationToken);
        return new(true, false, grants.Where(key => PermissionCatalog.Find(key) is { AdminOnly: false }).ToHashSet(StringComparer.Ordinal));
    }

    internal static async Task<ApplicationUser?> FindAuthenticatedUserAsync(IdentityDbContext db, ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var id = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var stamp = principal.FindFirstValue("SecurityStamp");
        var session = principal.FindFirstValue("SessionId");
        // The original SecurityStamp migration initialized existing accounts to "".
        // A present, matching legacy version is valid; a missing claim is not.
        if (principal.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(id) || stamp == null || string.IsNullOrEmpty(session)) return null;
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id && u.IsActive, cancellationToken);
        if (user == null || !string.Equals(user.SecurityStamp, stamp, StringComparison.Ordinal)) return null;
        return await db.UserSessions.AsNoTracking().AnyAsync(s => s.UserN == user.N && s.SessionId == session && s.IsActive, cancellationToken) ? user : null;
    }

    private sealed record AccessSnapshot(bool IsStaff, bool IsAdmin, HashSet<string> Permissions);
}
