using System.Globalization;
using System.Text.Json;

namespace Algara.Web.ViewModels.Admin;

/// <summary>Only known, non-secret audit fields can reach a view.</summary>
public static class CustomerManagementHistory
{
    private static readonly (string Key, string Label)[] ProfileFields =
        [("FirstName", "Име"), ("LastName", "Фамилия"), ("PhoneNumber", "Телефон"),
         ("FullName", "Пълно име"), ("DisplayName", "Показвано име")];
    private static readonly (string Key, string Label)[] AddressFields =
        [("FirstName", "Име"), ("LastName", "Фамилия"), ("PhoneNumber", "Телефон"), ("Email", "Имейл"),
         ("AddressLine1", "Адрес"), ("AddressLine2", "Допълнение към адреса"), ("City", "Град"),
         ("PostalCode", "Пощенски код"), ("Country", "Държава"), ("IsDefault", "Основен адрес")];
    private static readonly (string Key, string Label)[] AccessFields =
        [("IsActive", "Разрешен достъп"), ("AccessSuspendedAtUtc", "Спрян на"),
         ("FailedLoginAttempts", "Неуспешни опити"), ("LockoutUntil", "Блокиране до")];
    private static readonly (string Key, string Label)[] SessionFields =
        [("SessionN", "Номер на сесията"), ("ActiveSessionCount", "Непрекратени сесии"), ("IsActive", "Непрекратена")];
    private static readonly (string Key, string Label)[] PasswordResetFields =
        [("PasswordChanged", "Зададена нова парола"), ("RevokedSessionCount", "Прекратени сесии"),
         ("RevokedTokenCount", "Прекратени връзки за достъп"), ("CancelledEmailCount", "Отменени чакащи имейли")];
    private static readonly (string Key, string Label)[] AssistanceFields =
        [("SessionId", "Сесия за съдействие"), ("Mode", "Режим"), ("Operation", "Действие"), ("Result", "Резултат"),
         ("StartedAtUtc", "Начало"), ("ExpiresAtUtc", "Краен срок"), ("EndedAtUtc", "Край"),
         ("EndReason", "Причина за край"), ("OrderN", "Поръчка")];

    public static List<CustomerHistoryFieldChange> Format(string action, string? before, string? after)
    {
        try
        {
            using var beforeDoc = JsonDocument.Parse(string.IsNullOrWhiteSpace(before) ? "null" : before);
            using var afterDoc = JsonDocument.Parse(string.IsNullOrWhiteSpace(after) ? "null" : after);
            if (action is "AddressCreated" or "AddressUpdated" or "AddressDeleted" or "DefaultAddressChanged")
            {
                var oldRows = Addresses(beforeDoc.RootElement);
                var newRows = Addresses(afterDoc.RootElement);
                return oldRows.Keys.Union(newRows.Keys).OrderBy(n => n).SelectMany(n => Fields(
                    oldRows.GetValueOrDefault(n), newRows.GetValueOrDefault(n), AddressFields, $"Адрес #{n} · ")).ToList();
            }
            var fields = action switch
            {
                "ProfileUpdated" => ProfileFields,
                "AccountUnlocked" or "AccessSuspended" or "AccessRestored" => AccessFields,
                "SessionRevoked" or "AllSessionsRevoked" => SessionFields,
                "PasswordResetByStaff" => PasswordResetFields,
                "AssistanceStarted" or "AssistanceViewed" or "AssistanceOrderViewed" or "AssistanceEnded" or "AssistanceAction" => AssistanceFields,
                _ => []
            };
            return Fields(beforeDoc.RootElement, afterDoc.RootElement, fields, string.Empty);
        }
        catch (JsonException) { return []; }
    }

    private static Dictionary<int, JsonElement> Addresses(JsonElement element)
    {
        var result = new Dictionary<int, JsonElement>();
        if (element.ValueKind != JsonValueKind.Array) return result;
        foreach (var row in element.EnumerateArray())
            if (row.ValueKind == JsonValueKind.Object && row.TryGetProperty("N", out var number) && number.ValueKind == JsonValueKind.Number && number.TryGetInt32(out var n))
                result[n] = row;
        return result;
    }

    private static List<CustomerHistoryFieldChange> Fields(JsonElement before, JsonElement after,
        (string Key, string Label)[] fields, string prefix)
    {
        var changes = new List<CustomerHistoryFieldChange>();
        foreach (var (key, label) in fields)
        {
            var oldValue = Value(before, key);
            var newValue = Value(after, key);
            if (oldValue != newValue) changes.Add(new() { Label = prefix + label, Before = oldValue, After = newValue });
        }
        return changes;
    }

    private static string Value(JsonElement element, string key)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(key, out var value)) return "—";
        if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return "—";
        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False) return value.GetBoolean() ? "Да" : "Не";
        if (value.ValueKind == JsonValueKind.Number) return value.TryGetInt32(out var number) ? number.ToString(CultureInfo.InvariantCulture) : "—";
        if (value.ValueKind != JsonValueKind.String) return "—";
        var text = value.GetString();
        if (string.IsNullOrWhiteSpace(text)) return "—";
        if (key == "SessionId") return Guid.TryParse(text, out var id) ? id.ToString() : "—";
        if (key is "Mode" or "Operation" or "Result" or "EndReason") return text switch
        {
            "Preview" => "Само четене", "Assist" => "Ограничено съдействие",
            "Start" => "Начало", "End" => "Край", "ViewProfile" => "Преглед на профила", "ViewOrder" => "Преглед на поръчка",
            "ProfileUpdated" => "Промяна на профил", "AddressCreated" => "Нов адрес", "AddressUpdated" => "Промяна на адрес",
            "AddressDeleted" => "Изтриване на адрес", "DefaultAddressChanged" => "Основен адрес",
            "Succeeded" => "Успешно", "Rejected" => "Отказано", "Denied" => "Неразрешено", "NoChange" => "Без промяна",
            "ValidationRejected" => "Невалидни данни", "Expired" => "Изтекъл срок", "StaffEnded" => "Приключено от служителя",
            "InvalidContext" => "Невалидна сесия", "AccessChanged" => "Променен достъп", "TargetChanged" => "Променен клиентски акаунт",
            _ => "—"
        };
        if ((key == "LockoutUntil" || key.EndsWith("AtUtc", StringComparison.Ordinal)) && value.TryGetDateTime(out var date))
            return date.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture) + (key.EndsWith("Utc", StringComparison.Ordinal) ? " UTC" : string.Empty);
        return text.Length <= 1000 ? text : text[..1000];
    }
}
