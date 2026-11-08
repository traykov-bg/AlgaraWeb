using Algara.Data.Models;
using Algara.Data.Repositories;

namespace Algara.Web.Services.Cart;

public sealed class CartService(IProductRepository products, ICartStore store, TimeProvider clock, IStockReservationReader? reservations = null)
{
    public const int MaxLines = 30;
    public const int MaxQuantity = 99;

    public async Task<CartSnapshot> GetAsync()
    {
        var entries = store.Read();
        if (!AccountCartStore.ValidIdentities(entries)) return new CartSnapshot();
        var lines = new List<CartLine>(entries.Count);
        var loaded = new Dictionary<int, Product?>();
        var now = clock.GetLocalNow().DateTime;
        foreach (var entry in entries)
        {
            if (!loaded.TryGetValue(entry.ProductN, out var product))
            {
                product = await products.GetByNAsync(entry.ProductN);
                loaded[entry.ProductN] = product;
            }
            lines.Add(Evaluate(entry, product, now, reservations == null ? 0 : await reservations.ReservedAsync(entry.ProductN, entry.VariantN)));
        }
        return new CartSnapshot { Lines = lines };
    }

    public async Task<CartChangeResult> AddAsync(int productN, int? variantN, int quantity)
    {
        if (quantity is < 1 or > MaxQuantity) return new(CartLineIssue.InvalidQuantity);
        var entries = store.Read().ToList();
        var existing = entries.FindIndex(entry => entry.ProductN == productN && entry.VariantN == variantN);
        if (existing < 0 && entries.Count >= MaxLines) return new(CartLineIssue.LineLimit);
        var combinedQuantity = quantity + (existing < 0 ? 0 : entries[existing].Quantity);
        if (combinedQuantity > MaxQuantity) return new(CartLineIssue.InvalidQuantity);
        var entry = new CartEntry(productN, variantN, combinedQuantity);
        var line = Evaluate(entry, productN > 0 ? await products.GetByNAsync(productN) : null, clock.GetLocalNow().DateTime, reservations == null ? 0 : await reservations.ReservedAsync(productN, variantN));
        if (line.Issue != CartLineIssue.None) return new(line.Issue);
        if (existing < 0) entries.Add(entry); else entries[existing] = entry;
        store.Write(entries);
        return new(CartLineIssue.None);
    }

    public async Task<CartChangeResult> UpdateAsync(int productN, int? variantN, int quantity)
    {
        if (quantity is < 1 or > MaxQuantity) return new(CartLineIssue.InvalidQuantity);
        var entries = store.Read().ToList();
        var existing = entries.FindIndex(entry => entry.ProductN == productN && entry.VariantN == variantN);
        if (existing < 0) return new(CartLineIssue.MissingLine);
        var entry = new CartEntry(productN, variantN, quantity);
        var line = Evaluate(entry, await products.GetByNAsync(productN), clock.GetLocalNow().DateTime, reservations == null ? 0 : await reservations.ReservedAsync(productN, variantN));
        if (line.Issue != CartLineIssue.None) return new(line.Issue);
        entries[existing] = entry;
        store.Write(entries);
        return new(CartLineIssue.None);
    }

    public void Remove(int productN, int? variantN)
        => store.Write(store.Read().Where(entry => entry.ProductN != productN || entry.VariantN != variantN).ToList());

    public void Clear() => store.Write([]);

    public static CartLine Evaluate(CartEntry entry, Product? product, DateTime now, int reserved = 0)
    {
        CartLine Invalid(CartLineIssue issue) => new() { Entry = entry, Product = product, Issue = issue };
        if (entry.Quantity is < 1 or > MaxQuantity) return Invalid(CartLineIssue.InvalidQuantity);
        if (product == null || product.N != entry.ProductN) return Invalid(CartLineIssue.MissingProduct);
        if (!product.IsActive) return Invalid(CartLineIssue.InactiveProduct);
        ProductVariant? variant = null;
        if (product.OptionKind == ProductOptionKind.None)
        {
            if (entry.VariantN.HasValue) return Invalid(CartLineIssue.InvalidVariant);
        }
        else
        {
            variant = product.GetSelectableVariants().SingleOrDefault(candidate => candidate.N == entry.VariantN);
            if (variant == null) return Invalid(CartLineIssue.InvalidVariant);
        }
        var price = variant == null ? product.GetDiscountedPrice(now) : product.GetVariantPrice(variant, now);
        if (price <= 0 || price > 99_999_999m) return Invalid(CartLineIssue.InvalidPrice);
        var stock = variant?.StockQuantity ?? (variant == null ? product.StockQuantity : null);
        if (stock.HasValue) stock = Math.Max(0, stock.Value - reserved);
        var issue = stock switch
        {
            null or < 0 => CartLineIssue.UnknownStock,
            0 when !product.IsUpholstered => CartLineIssue.InquiryRequired,
            _ when stock < entry.Quantity && !product.IsUpholstered => CartLineIssue.InsufficientStock,
            _ => CartLineIssue.None
        };
        var availability = stock == null || stock < 0 ? ProductAvailability.Unknown :
            stock < entry.Quantity && product.IsUpholstered ? ProductAvailability.MadeToOrder :
            DeliveryPolicy.GetAvailability(stock, product.IsUpholstered);
        return new CartLine
        {
            Entry = entry, Product = product, Variant = variant, UnitPrice = price,
            Availability = availability, Issue = issue
        };
    }
}
