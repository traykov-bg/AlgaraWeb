using Algara.Data.Models;
using Algara.Data.Repositories;
using Algara.Web.Services.Cart;
using Algara.Web.ViewModels.Admin;
using Microsoft.EntityFrameworkCore;
namespace Algara.Web.Services.Checkout;

public sealed partial class StaffOrderService
{
    public async Task<CartSnapshot> ReadCartAsync(StaffOrderForm form)
    {
        if (!form.InquiryN.HasValue) return await ReadCartAsync(form.Lines);
        var offer = await db.Set<InquiryOffer>().AsNoTracking().SingleOrDefaultAsync(o => o.N == form.InquiryOfferN && o.InquiryN == form.InquiryN);
        if (offer?.LinesJson == null) return new();
        form.AgreedLines = AgreedOffer.Lines(offer.LinesJson);
        form.AgreedServices = AgreedOffer.Services(offer.ServicesJson);
        form.Lines = form.AgreedLines.Select(l => new CartEntry(l.ProductN,l.VariantN,l.Quantity)).ToList();
        var lines = new List<CartLine>();
        foreach (var agreed in form.AgreedLines)
        {
            var p = await new ProductRepository(db).GetByNAsync(agreed.ProductN);
            var v = p?.GetSelectableVariants().SingleOrDefault(v => v.N == agreed.VariantN);
            var valid = p is { IsActive:true } && (p.OptionKind == ProductOptionKind.None ? agreed.VariantN == null : v != null);
            // Catalog package dimensions do not describe an individual build.

            lines.Add(new() { Entry=new(agreed.ProductN,agreed.VariantN,agreed.Quantity),Product=p,Variant=v,UnitPrice=agreed.UnitPrice,
                IndividualManufacture=agreed.IndividualManufacture,Issue=valid ? CartLineIssue.None : CartLineIssue.InvalidVariant });
        }
        return new() { Lines=lines };
    }
}
