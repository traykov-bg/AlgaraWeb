namespace Algara.Identity.Data;

public sealed class Permission
{
    public string Key { get; set; } = string.Empty;
    public string Module { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool AdminOnly { get; set; }
}

public sealed class RolePermission
{
    public int RoleN { get; set; }
    public string PermissionKey { get; set; } = string.Empty;
    public ApplicationRole Role { get; set; } = null!;
    public Permission Permission { get; set; } = null!;
}

public sealed class AccessAuditEvent
{
    public long Id { get; set; }
    public string ActorId { get; set; } = string.Empty;
    public string ActorName { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
    public string Action { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string TargetName { get; set; } = string.Empty;
    public string BeforeJson { get; set; } = string.Empty;
    public string AfterJson { get; set; } = string.Empty;
}
