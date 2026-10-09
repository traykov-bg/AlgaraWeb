using System.Globalization;

namespace Algara.Data.Models;

public static class ProductSku
{
    public const int MaxLength = 64;

    public static string Normalize(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant();

    public static bool IsValid(string? value)
        => !string.IsNullOrEmpty(value) && value.Length <= MaxLength &&
           value.All(c => c is >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_');

    public static string FormatNumber(long number)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(number);
        return number.ToString("D6", CultureInfo.InvariantCulture);
    }
}
