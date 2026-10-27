namespace Algara.Identity.Authorization;

/// <summary>Role hierarchy only. Callers also enforce live permissions, identity, target state and self restrictions.</summary>
public static class StaffAccountManagementRules
{
    public static bool CanManage(IEnumerable<string> actorRoles, IEnumerable<string> targetRoles)
    {
        var actor = actorRoles.ToHashSet(StringComparer.Ordinal);
        if (actor.Contains(StaffRoles.Admin)) return true;
        if (!actor.Any(StaffRoles.IsStaff) || actor.Any(role => role != "User" && !StaffRoles.IsStaff(role))) return false;
        var target = targetRoles.ToArray();
        if (actor.Contains(StaffRoles.Moderator))
            return target.All(role => role is "User" or StaffRoles.CatalogEditor or StaffRoles.OrderManager);
        return target.All(role => role == "User");
    }

    public static bool CanResetPassword(IEnumerable<string> actorRoles, IEnumerable<string> targetRoles)
    {
        var actor = actorRoles.ToArray();
        return actor.Any(role => role is StaffRoles.Admin or StaffRoles.Moderator) && CanManage(actor, targetRoles);
    }
}
