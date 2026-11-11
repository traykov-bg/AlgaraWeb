using Algara.Data.Models;
using System.Globalization;
namespace Algara.Web.Services.Inquiries;
public static class InquiryProductSelection
{
    public static string? Describe(Product product, int? variantN, int quantity, string language)
    {
        if (!product.IsActive || quantity is < 1 or > 99) return null;
        var variant = product.GetSelectableVariants().SingleOrDefault(x => x.N == variantN);
        if (product.OptionKind == ProductOptionKind.None ? variantN != null : variant == null) return null;
        var en = language == "en";
        var parts = new List<string> { $"{product.GetName(language)} (#{product.N})", $"SKU: {variant?.Sku ?? product.Sku}" };
        if (variant != null)
        {
            parts.Add((en ? "Selection: " : "Избор: ") +
                (variant.Color.Fabric == null ? "" : variant.Color.Fabric.GetName(language) + " / ") + variant.Color.GetName(language) + $" (#{variant.N})");
        }
        parts.Add((en ? "Quantity: " : "Количество: ") + quantity.ToString(CultureInfo.InvariantCulture));
        return string.Join("\n", parts);
    }
    public static string Dimensions(Product product) => string.Join("; ", new[] {
        product.WidthCm.HasValue ? $"W: {product.WidthCm:0.##} cm" : null,
        product.HeightCm.HasValue ? $"H: {product.HeightCm:0.##} cm" : null,
        product.DepthCm.HasValue ? $"D: {product.DepthCm:0.##} cm" : null }.Where(x => x != null));
}
