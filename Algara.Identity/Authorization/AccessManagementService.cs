using System.Security.Claims;
using System.Text.Json;
using Algara.Identity.Data;
using Algara.Identity.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Algara.Identity.Authorization;

public sealed record AccessChangeResult(bool Succeeded, bool Changed, string? Error = null)
{
    public static AccessChangeResult Reject(string error) => new(false, false, error);
    public static AccessChangeResult Success(bool changed = true) => new(true, changed);
}

/// <summary>All access changes, including self-deletion, share one cross-process SQL lock.</summary>
public sealed class AccessManagementService(IdentityDbContext db)
{
    public async Task<AccessChangeResult> SetUserRoleAsync(ClaimsPrincipal actor, string targetUserId, string roleName,
        bool add, string? expectedSecurityStamp, CancellationToken cancellationToken = default)
    {
        if ((!StaffRoles.IsStaff(roleName) && roleName != "User") || expectedSecurityStamp == null)
            return AccessChangeResult.Reject("Невалидна роля или версия. Презаредете страницата.");
        await using var transaction = await AcquireAsync(cancellationToken);
        var administrator = await FindAdministratorAsync(actor, cancellationToken);
        if (administrator == null) return AccessChangeResult.Reject("Само администратор може да променя роли.");
        var target = await LoadTargetAsync(targetUserId, cancellationToken);
        if (target == null || !target.IsActive) return AccessChangeResult.Reject("Потребителят не е активен или не съществува.");
        if (target.SecurityStamp != expectedSecurityStamp) return AccessChangeResult.Reject("Потребителят е променен. Презаредете страницата.");
        var role = await db.Roles.SingleOrDefaultAsync(r => r.Name == roleName, cancellationToken);
        if (role == null) return AccessChangeResult.Reject("Ролята не съществува.");
        var assignment = await db.UserRoles.SingleOrDefaultAsync(r => r.UserN == target.N && r.RoleN == role.N, cancellationToken);
        if (add == (assignment != null)) return AccessChangeResult.Success(false);
        if (!add && roleName == StaffRoles.Admin && await IsLastAdministratorAsync(target.N, cancellationToken))
            return AccessChangeResult.Reject("Последният активен администратор трябва да запази ролята си.");
        var before = await RoleNamesAsync(target.N, cancellationToken);
        if (add) db.UserRoles.Add(new UserRole { UserN = target.N, RoleN = role.N });
        else db.UserRoles.Remove(assignment!);
        target.SecurityStamp = Guid.NewGuid().ToString();
        var after = add ? before.Append(roleName).Order(StringComparer.Ordinal).ToArray() : before.Where(r => r != roleName).ToArray();
        Audit(administrator, add ? "RoleAssigned" : "RoleRemoved", target.Id, target.UserName, before, after);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return AccessChangeResult.Success();
    }

    public async Task<AccessChangeResult> SaveRolePermissionsAsync(ClaimsPrincipal actor, string roleName,
        IEnumerable<string> permissionKeys, long expectedVersion, CancellationToken cancellationToken = default)
    {
        if (!StaffRoles.IsStaff(roleName) || roleName == StaffRoles.Admin)
            return AccessChangeResult.Reject("Пълните права на Администратора са защитени. Матрицата е само за служебни роли.");
        var submitted = permissionKeys?.Take(PermissionCatalog.All.Count + 1).ToArray() ?? [];
        if (submitted.Length > PermissionCatalog.All.Count || submitted.Any(k => PermissionCatalog.Find(k) is not { AdminOnly: false }))
            return AccessChangeResult.Reject("Списъкът съдържа неизвестни или запазени за Администратора права.");
        var keys = submitted.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (keys.Contains(PermissionCatalog.Users.ResetPassword, StringComparer.Ordinal) && roleName != StaffRoles.Moderator)
            return AccessChangeResult.Reject("Задаването на нова парола е достъпно само за Администратор и Модератор.");
        foreach (var key in keys)
        {
            var view = key.Split('.')[0] + ".View";
            if (PermissionCatalog.Find(view) != null && !keys.Contains(view, StringComparer.Ordinal))
                return AccessChangeResult.Reject("За разрешените действия е необходимо и право за преглед на съответния раздел.");
        }
        if (keys.Contains(PermissionCatalog.Products.Create, StringComparer.Ordinal) && !keys.Contains(PermissionCatalog.Products.ChangePrice, StringComparer.Ordinal))
            return AccessChangeResult.Reject("Създаването на продукт изисква право за задаване на цена.");
        if (keys.Any(k => k.StartsWith("PartnerGroups.", StringComparison.Ordinal)) && !keys.Contains(PermissionCatalog.Partners.View, StringComparer.Ordinal))
            return AccessChangeResult.Reject("Работата с групи изисква преглед на контрагентите, където се намира дървото на групите.");
        if (keys.Any(PermissionCatalog.RequiresCustomerDossier) &&
            !keys.Contains(PermissionCatalog.Users.ViewDossier, StringComparer.Ordinal))
            return AccessChangeResult.Reject("Действията и историята на клиентския акаунт изискват право за преглед на досието.");
        if (keys.Contains(PermissionCatalog.Users.RevokeSessions, StringComparer.Ordinal) &&
            !keys.Contains(PermissionCatalog.Users.ViewSessions, StringComparer.Ordinal))
            return AccessChangeResult.Reject("Прекратяването на сесии изисква право за преглед на сесиите.");
        if (keys.Contains(PermissionCatalog.Users.ManagePartnerLinks, StringComparer.Ordinal) && !keys.Contains(PermissionCatalog.Partners.View, StringComparer.Ordinal))
            return AccessChangeResult.Reject("Свързването на акаунт изисква право за преглед на контрагентите.");
        await using var transaction = await AcquireAsync(cancellationToken);
        var administrator = await FindAdministratorAsync(actor, cancellationToken);
        if (administrator == null) return AccessChangeResult.Reject("Само администратор може да променя матрицата на правата.");
        var role = await db.Roles.SingleOrDefaultAsync(r => r.Name == roleName, cancellationToken);
        if (role == null) return AccessChangeResult.Reject("Ролята не съществува.");
        await db.Entry(role).ReloadAsync(cancellationToken);
        if (role.PermissionsVersion != expectedVersion) return AccessChangeResult.Reject("Правата са променени от друг администратор. Презаредете страницата.");
        var existing = await db.RolePermissions.Where(p => p.RoleN == role.N).ToListAsync(cancellationToken);
        var before = existing.Select(p => p.PermissionKey).Order(StringComparer.Ordinal).ToArray();
        if (before.SequenceEqual(keys, StringComparer.Ordinal)) return AccessChangeResult.Success(false);
        var catalogKeys = await db.Permissions.AsNoTracking().Where(p => keys.Contains(p.Key)).Select(p => p.Key).ToListAsync(cancellationToken);
        if (catalogKeys.Count != keys.Length) return AccessChangeResult.Reject("Картотеката на правата не е актуална. Приложете миграцията.");
        db.RolePermissions.RemoveRange(existing.Where(p => !keys.Contains(p.PermissionKey, StringComparer.Ordinal)));
        foreach (var key in keys.Except(before, StringComparer.Ordinal)) db.RolePermissions.Add(new RolePermission { RoleN = role.N, PermissionKey = key });
        role.PermissionsVersion = checked(role.PermissionsVersion + 1);
        Audit(administrator, "RolePermissionsChanged", role.Name, role.Name, before, keys);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return AccessChangeResult.Success();
    }

    public async Task<AccessChangeResult> DeleteOwnAccountAsync(ClaimsPrincipal actor, string expectedSecurityStamp,
        string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        await using var transaction = await AcquireAsync(cancellationToken);
        var authenticated = await StaffAccessService.FindAuthenticatedUserAsync(db, actor, cancellationToken);
        if (authenticated == null) return AccessChangeResult.Reject("Сесията е невалидна. Влезте отново.");
        var user = (await LoadTargetAsync(authenticated.Id, cancellationToken))!;
        if (user.SecurityStamp != expectedSecurityStamp) return AccessChangeResult.Reject("Акаунтът е променен. Презаредете страницата.");
        if (await IsLastAdministratorAsync(user.N, cancellationToken)) return AccessChangeResult.Reject("Последният активен администратор не може да изтрие акаунта си. Първо определете друг администратор.");
        var roles = await RoleNamesAsync(user.N, cancellationToken);
        Audit(authenticated, "AccountDeactivated", user.Id, user.UserName, new { IsActive = true, Roles = roles }, new { IsActive = false, Roles = roles });
        user.IsActive = false;
        user.FirstName = null; user.LastName = null;
        user.FullName = "Изтрит потребител"; user.DisplayName = "Изтрит потребител";
        user.PhoneNumber = null; user.AddressLine1 = null; user.AddressLine2 = null;
        user.City = null; user.PostalCode = null; user.Country = null; user.MarketingConsent = false;
        user.Email = $"deleted-{user.N}-{Guid.NewGuid():N}@deleted.local"; user.UserName = user.Email;
        user.SecurityStamp = Guid.NewGuid().ToString();
        db.UsersAddresses.RemoveRange(await db.UsersAddresses.Where(a => a.UserN == user.N).ToListAsync(cancellationToken));
        foreach (var session in await db.UserSessions.Where(s => s.UserN == user.N && s.IsActive).ToListAsync(cancellationToken)) session.IsActive = false;
        db.UserConsents.Add(new UserConsent { UserN = user.N, ConsentType = ConsentTypes.Marketing, Granted = false, PolicyVersion = "1.0", ConsentedAt = DateTime.UtcNow, IpAddress = ipAddress, UserAgent = userAgent });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return AccessChangeResult.Success();
    }

    /// <summary>Development seeding only: never replaces, demotes, or overrides an existing active administrator.</summary>
    public async Task<AccessChangeResult> BootstrapAdminAsync(string username, CancellationToken cancellationToken = default)
    {
        await using var transaction = await AcquireAsync(cancellationToken);
        if (await db.UserRoles.AnyAsync(r => r.Role.Name == StaffRoles.Admin && r.User.IsActive, cancellationToken)) return AccessChangeResult.Success(false);
        var user = await db.Users.SingleOrDefaultAsync(u => u.UserName == username && u.IsActive, cancellationToken);
        var role = await db.Roles.SingleOrDefaultAsync(r => r.Name == StaffRoles.Admin, cancellationToken);
        if (user == null || role == null) return AccessChangeResult.Reject("Липсва началният администратор или неговата роля.");
        db.UserRoles.Add(new UserRole { UserN = user.N, RoleN = role.N });
        user.SecurityStamp = Guid.NewGuid().ToString();
        db.AccessAuditEvents.Add(new AccessAuditEvent { ActorId = "system:development-seed", ActorName = "Начална настройка", Action = "AdminBootstrapped", Target = user.Id, TargetName = user.UserName, BeforeJson = "[]", AfterJson = JsonSerializer.Serialize(new[] { StaffRoles.Admin }) });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return AccessChangeResult.Success();
    }

    private async Task<ApplicationUser?> FindAdministratorAsync(ClaimsPrincipal actor, CancellationToken cancellationToken)
    {
        var user = await StaffAccessService.FindAuthenticatedUserAsync(db, actor, cancellationToken);
        return user != null && await db.UserRoles.AsNoTracking().AnyAsync(r => r.UserN == user.N && r.Role.Name == StaffRoles.Admin, cancellationToken) ? user : null;
    }

    private async Task<ApplicationUser?> LoadTargetAsync(string id, CancellationToken cancellationToken)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user != null) await db.Entry(user).ReloadAsync(cancellationToken);
        return user;
    }

    private async Task<bool> IsLastAdministratorAsync(int userN, CancellationToken cancellationToken) =>
        await db.UserRoles.AnyAsync(r => r.UserN == userN && r.Role.Name == StaffRoles.Admin && r.User.IsActive, cancellationToken) &&
        !await db.UserRoles.AnyAsync(r => r.UserN != userN && r.Role.Name == StaffRoles.Admin && r.User.IsActive, cancellationToken);

    private Task<string[]> RoleNamesAsync(int userN, CancellationToken cancellationToken) => db.UserRoles.AsNoTracking()
        .Where(r => r.UserN == userN).Select(r => r.Role.Name).OrderBy(r => r).ToArrayAsync(cancellationToken);

    private void Audit(ApplicationUser actor, string action, string target, string targetName, object before, object after) =>
        db.AccessAuditEvents.Add(new AccessAuditEvent
        {
            ActorId = actor.Id, ActorName = string.IsNullOrWhiteSpace(actor.DisplayName) ? actor.UserName : actor.DisplayName,
            OccurredAtUtc = DateTime.UtcNow, Action = action, Target = target, TargetName = targetName,
            BeforeJson = JsonSerializer.Serialize(before), AfterJson = JsonSerializer.Serialize(after)
        });

    private async Task<IDbContextTransaction> AcquireAsync(CancellationToken cancellationToken)
    {
        var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await db.Database.ExecuteSqlRawAsync("""
                DECLARE @result int;
                EXEC @result = sys.sp_getapplock @Resource=N'Algara.AccessManagement', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=15000;
                IF @result < 0 THROW 51004, 'Access management is busy. Retry the operation.', 1;
                """, cancellationToken);
            return transaction;
        }
        catch { await transaction.DisposeAsync(); throw; }
    }
}
