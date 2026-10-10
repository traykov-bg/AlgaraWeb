using System.Globalization;
using System.Text;

namespace Algara.Data.Models;

/// <summary>Human-entered catalog labels, independent of product SKUs.</summary>
public static class CatalogCode
{
    public const int MaxLength = 64;
    public const int FabricMaxLength = 200;

    // Keep user spelling and punctuation. NFC avoids distinct encodings of the same label.
    public static string Normalize(string? value)
    {
        var result = (value ?? string.Empty).Trim();
        try { return result.Normalize(NormalizationForm.FormC); }
        catch (ArgumentException) { return result; }
    }

    public static string FromName(string? name)
        => Normalize(string.Concat((name ?? string.Empty).Where(c => !char.IsWhiteSpace(c))));

    public static bool IsValid(string? value, int maxLength = MaxLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength) return false;
        foreach (var rune in value.EnumerateRunes())
        {
            if (Rune.GetUnicodeCategory(rune) is UnicodeCategory.Control or UnicodeCategory.Format
                or UnicodeCategory.Surrogate or UnicodeCategory.OtherNotAssigned or UnicodeCategory.PrivateUse
                or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)
                return false;
            if (rune.Value is 0x034F or 0x115F or 0x1160 or 0x17B4 or 0x17B5 or 0x3164
                or 0xFFA0 or 0xFFFD or >= 0x180B and <= 0x180F
                or >= 0xFE00 and <= 0xFE0F or >= 0xE0100 and <= 0xE01EF)
                return false;
        }
        return true;
    }
}
