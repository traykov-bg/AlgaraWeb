using System.Text.Json;
namespace Algara.Data.Models;

public sealed class AgreedOfferLine
{
    public int ProductN { get; set; }
    public int? VariantN { get; set; }
    public int Quantity { get; set; } = 1;
    public string? Name { get; set; } = "";
    public string? VariantName { get; set; } = "";
    public string? Sku { get; set; } = "";
    public decimal CatalogUnitPrice { get; set; }
    public decimal UnitPrice { get; set; }
    public string? Comment { get; set; } = "";
    public bool IndividualManufacture { get; set; }
}
public sealed record AgreedService(string Name, decimal Amount);
public static class AgreedOffer
{
    public static List<AgreedOfferLine> Lines(string? json) => json == null ? [] : JsonSerializer.Deserialize<List<AgreedOfferLine>>(json)!;
    public static List<AgreedService> Services(string? json) => json == null ? [] : JsonSerializer.Deserialize<List<AgreedService>>(json)!;
    public static bool Money(decimal amount, bool zero = false) => amount >= (zero ? 0m : 0.01m) && amount <= 999999999m && decimal.Round(amount, 2) == amount;
    public static bool ValidServices(IReadOnlyList<AgreedService> services) => services.Count <= 20 && services.All(s => s != null && !string.IsNullOrWhiteSpace(s.Name) && s.Name.Length <= 100 && !s.Name.Any(char.IsControl) && Money(s.Amount, true));
    public static (List<AgreedService> Remaining, List<AgreedService> Separated)? SplitServices(IReadOnlyList<AgreedService> services, IReadOnlyDictionary<int, decimal> amounts)
    {
        if (amounts.Keys.Any(i => i < 0 || i >= services.Count)) return null;
        var left = new List<AgreedService>(); var right = new List<AgreedService>();
        for (var i = 0; i < services.Count; i++)
        {
            var moved = amounts.GetValueOrDefault(i);
            if (!Money(moved, true) || moved > services[i].Amount) return null;
            // Keep zero-valued named services: the description is part of the agreement.
            left.Add(services[i] with { Amount = services[i].Amount - moved });
            if (moved > 0) right.Add(services[i] with { Amount = moved });
        }
        return (left, right);
    }
}
