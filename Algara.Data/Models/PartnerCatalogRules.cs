using System.Text;
using System.Text.RegularExpressions;

namespace Algara.Data.Models;

public static partial class PartnerCatalogRules
{
    public static string? Text(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : Spaces().Replace(value.Normalize(NormalizationForm.FormC).Trim(), " ");

    public static string? Key(string? value) => Text(value)?.ToUpperInvariant();

    public static string? Identifier(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : Spaces().Replace(value.Normalize(NormalizationForm.FormC), "").ToUpperInvariant();

    public static bool IsCode(string value) => value.Length is > 0 and <= 32 && value.All(c => c is >= '0' and <= '9');

    // Local numbers use Bulgaria. Other countries must include an explicit + or 00 prefix.
    public static bool TryPhone(string? value, out string? phone)
    {
        phone = null;
        if (string.IsNullOrWhiteSpace(value)) return true;
        var compact = string.Concat(value.Where(c => !char.IsWhiteSpace(c) && c is not '-' and not '(' and not ')' and not '.'));
        if (compact.StartsWith("00", StringComparison.Ordinal)) compact = "+" + compact[2..];
        else if (compact.StartsWith('0')) compact = "+359" + compact[1..];
        if (compact.Length is < 8 or > 16 || compact[0] != '+' || compact[1] is < '1' or > '9' ||
            !compact.AsSpan(1).ToArray().All(c => c is >= '0' and <= '9')) return false;
        phone = compact;
        return true;
    }

    public static bool TryPartnerPhone(string? value, out string? phone)
    {
        if (value?.Trim() == "0") { phone = "0"; return true; }
        return TryPhone(value, out phone);
    }

    public static bool WouldCreateCycle(IEnumerable<PartnerGroup> groups, int groupN, int? parentN)
    {
        var parents = groups.ToDictionary(g => g.N, g => g.ParentN);
        var visited = new HashSet<int> { groupN };
        while (parentN.HasValue)
        {
            if (!visited.Add(parentN.Value)) return true;
            if (!parents.TryGetValue(parentN.Value, out parentN)) return false;
        }
        return false;
    }

    public static HashSet<int> Descendants(IEnumerable<PartnerGroup> groups, int root)
    {
        var children = groups.ToLookup(g => g.ParentN);
        var result = new HashSet<int>();
        var pending = new Stack<int>();
        pending.Push(root);
        while (pending.TryPop(out var next))
            if (result.Add(next)) foreach (var child in children[next]) pending.Push(child.N);
        return result;
    }

    [GeneratedRegex(@"\s+")] private static partial Regex Spaces();
}
