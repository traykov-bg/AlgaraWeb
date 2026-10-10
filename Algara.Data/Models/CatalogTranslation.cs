namespace Algara.Data.Models;

internal static class CatalogTranslation
{
    public static string? Value(string? original, string? english, string? language)
    {
        language ??= System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        var useEnglish = language.Equals("en", StringComparison.OrdinalIgnoreCase) ||
                         language.StartsWith("en-", StringComparison.OrdinalIgnoreCase);
        return useEnglish && !string.IsNullOrWhiteSpace(english) ? english : original;
    }
}
