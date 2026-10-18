using Algara.Identity.Authorization;
using Algara.Identity.Data;
using System.ComponentModel.DataAnnotations;

namespace Algara.Web.ViewModels.Admin;

public sealed class AdminAccessFormViewModel
{
    [Required, StringLength(100)]
    public string RoleName { get; set; } = StaffRoles.Moderator;
    [Range(0, long.MaxValue)]
    public long ExpectedVersion { get; set; }
    public List<string> PermissionKeys { get; set; } = [];
    public List<string> AvailableRoles { get; set; } = [];
    public IReadOnlyList<PermissionDefinition> Definitions => PermissionCatalog.All;
    public bool IsReadOnly => RoleName == StaffRoles.Admin;

    public static string RoleLabel(string name) => name switch
    {
        StaffRoles.Admin => "Администратор",
        StaffRoles.Moderator => "Модератор",
        StaffRoles.CatalogEditor => "Редактор на каталог",
        StaffRoles.OrderManager => "Служител за поръчки",
        "User" => "Клиент",
        _ => name
    };
}

public sealed class AdminAccessHistoryViewModel
{
    public List<AccessAuditEvent> Events { get; set; } = [];
    public int Page { get; set; } = 1;
    public bool HasNextPage { get; set; }
}
