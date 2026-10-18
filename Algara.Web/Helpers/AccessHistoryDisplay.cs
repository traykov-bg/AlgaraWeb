using Algara.Identity.Authorization;
using Algara.Web.ViewModels.Admin;
using System.Text.Json;

namespace Algara.Web.Helpers;

public static class AccessHistoryDisplay
{
    public static string ActionLabel(string action) => action switch
    {
        "RoleAssigned" => "Добавена роля",
        "RoleRemoved" => "Отнета роля",
        "RolePermissionsChanged" => "Променени права",
        "AccountDeactivated" => "Изтрит профил",
        "AdminBootstrapped" => "Начален администратор",
        _ => action
    };

    public static string Snapshot(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return Describe(document.RootElement);
        }
        catch (JsonException) { return "Недостъпни подробности"; }
    }

    private static string Describe(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            var values = element.EnumerateArray().Select(Describe).ToArray();
            return values.Length == 0 ? "Няма" : string.Join("; ", values);
        }
        if (element.ValueKind == JsonValueKind.String)
        {
            var value = element.GetString() ?? string.Empty;
            var permission = PermissionCatalog.Find(value);
            return permission == null ? AdminAccessFormViewModel.RoleLabel(value) : $"{permission.Module}: {permission.Name}";
        }
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty("IsActive", out var active))
        {
            var status = active.GetBoolean() ? "Активен профил" : "Изтрит профил";
            return element.TryGetProperty("Roles", out var roles) ? $"{status}. Роли: {Describe(roles)}" : status;
        }
        return "—";
    }
}
