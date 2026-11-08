using System.Text.Json.Serialization;
using Algara.Data.Models;

namespace Algara.Web.Services.Cart;

public sealed record CartEntry(
    [property: JsonPropertyName("p")] int ProductN,
    [property: JsonPropertyName("v")] int? VariantN,
    [property: JsonPropertyName("q")] int Quantity);

public interface ICartStore
{
    IReadOnlyList<CartEntry> Read();
    void Write(IReadOnlyList<CartEntry> entries);
}

public enum CartLineIssue
{
    None, MissingProduct, InactiveProduct, InvalidVariant, UnknownStock,
    InquiryRequired, InsufficientStock, InvalidPrice, InvalidQuantity, LineLimit, MissingLine
}

public sealed class CartLine
{
    public required CartEntry Entry { get; init; }
    public Product? Product { get; init; }
    public ProductVariant? Variant { get; init; }
    public decimal? UnitPrice { get; init; }
    public decimal? LineTotal => UnitPrice * Entry.Quantity;
    public ProductAvailability Availability { get; init; }
    public CartLineIssue Issue { get; init; }
    public string Name => Product?.GetName() ?? string.Empty;
    public string VariantName => Variant?.Color is not { } color ? string.Empty :
        color.Fabric is { } fabric ? fabric.GetName() + " / " + color.GetName() : color.GetName();
    public string Sku => Variant?.Sku ?? Product?.Sku ?? string.Empty;
    public string? ImageUrl => Product?.GetVariantImageUrl(Variant);
}

public sealed class CartSnapshot
{
    public IReadOnlyList<CartLine> Lines { get; init; } = [];
    public decimal GoodsSubtotal => Lines.Sum(line => line.LineTotal ?? 0m);
    public bool CanQuote => Lines.Count > 0 && Lines.Count <= CartService.MaxLines && Lines.All(line => line.Issue == CartLineIssue.None);
    public int ItemCount => Lines.Sum(line => line.Entry.Quantity);
}

public sealed record CartChangeResult(CartLineIssue Issue)
{
    public bool Success => Issue == CartLineIssue.None;
}
