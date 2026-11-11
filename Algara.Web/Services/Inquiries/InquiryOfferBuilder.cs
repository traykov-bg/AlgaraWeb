using Algara.Data.Data;
using Algara.Data.Models;
using Algara.Data.Repositories;
using Algara.Web.Services.Cart;
using Algara.Web.ViewModels;
using System.Text.Json;
namespace Algara.Web.Services.Inquiries;

public static class InquiryOfferBuilder
{
    public static async Task<AgreedOfferLine?> SelectionAsync(ShopDbContext db, int productN, int? variantN, int quantity, DateTime now)
    {
        var p = await new ProductRepository(db).GetByNAsync(productN);
        return Selection(p,variantN,quantity,now);
    }

    public static AgreedOfferLine? Selection(Product? p, int? variantN, int quantity, DateTime now)
    {
        if (p == null || !p.IsActive || quantity is < 1 or > 99) return null;
        var v = variantN.HasValue ? p.GetSelectableVariants().SingleOrDefault(x => x.N == variantN) : null;
        if (p.OptionKind == ProductOptionKind.None ? variantN.HasValue : v == null) return null;
        var line = new CartLine { Entry = new(p.N, variantN, quantity), Product = p, Variant = v };
        var price = v == null ? p.GetDiscountedPrice(now) : p.GetVariantPrice(v, now);
        return new() { ProductN = p.N, VariantN = variantN, Quantity = quantity, Name = line.Name, VariantName = line.VariantName,
            Sku = line.Sku, CatalogUnitPrice = price, UnitPrice = price, IndividualManufacture = p.IsStaffOnly };
    }

    public static async Task<InquiryOffer?> BuildAsync(ShopDbContext db, InquiryUpdate form, string actor, DateTime now)
    {
        if (form.Lines.Count is < 1 or > 30 || !AgreedOffer.ValidServices(form.Services) ||
            (form.DeliveryAmount.HasValue && !AgreedOffer.Money(form.DeliveryAmount.Value, true)) ||
            string.IsNullOrWhiteSpace(form.Terms) || form.Terms.Length > 1000 ||
            string.IsNullOrWhiteSpace(form.Description) || form.Description.Length > 4000) return null;
        var lines = new List<AgreedOfferLine>();
        foreach (var input in form.Lines)
        {
            var line = await SelectionAsync(db, input.ProductN, input.VariantN, input.Quantity, now);
            if (line == null || !AgreedOffer.Money(input.UnitPrice) || input.Comment?.Length > 6000) return null;
            line.UnitPrice = input.UnitPrice;
            line.IndividualManufacture |= input.IndividualManufacture;
            line.Comment = input.Comment?.Trim() ?? "";
            if ((line.IndividualManufacture || line.UnitPrice != line.CatalogUnitPrice) && string.IsNullOrWhiteSpace(line.Comment)) return null;
            lines.Add(line);
        }
        var total = lines.Sum(x => x.UnitPrice * x.Quantity) + form.Services.Sum(x => x.Amount) + (form.DeliveryAmount ?? 0);
        if (!AgreedOffer.Money(total)) return null;
        return new() { LinesJson = JsonSerializer.Serialize(lines), ServicesJson = JsonSerializer.Serialize(form.Services),
            DeliveryAmount = form.DeliveryAmount, Price = total, Terms = form.Terms.Trim(), Description = form.Description.Trim(), ActorId = actor, AtUtc = now };
    }
}
