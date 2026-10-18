using Microsoft.AspNetCore.Authorization;

namespace Algara.Web.Authorization;

/// <summary>Every named permission is required; the role-to-permission assignments live in the database.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class RequirePermissionAttribute : AuthorizeAttribute
{
    public const string Prefix = "Permission:";
    public const string StaffPolicy = "Staff";

    public string[] Permissions { get; }
    public RequirePermissionAttribute(params string[] permissions)
    {
        Permissions = permissions;
        Policy = Prefix + string.Join(',', permissions);
    }
}
