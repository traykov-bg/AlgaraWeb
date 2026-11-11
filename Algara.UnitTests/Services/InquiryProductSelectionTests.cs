using Algara.Data.Models;
using Algara.Web.Services.Inquiries;
namespace Algara.UnitTests.Services;
public sealed class InquiryProductSelectionTests
{
    private static Product Product()
    {
        var p = new Product { N = 5, Name = "Desk", Sku = "DESK", OptionKind = ProductOptionKind.Color };
        p.Variants.Add(new ProductVariant { N = 8, ProductN = 5, Sku = "BLUE-DESK", Color = new CatalogColor { Name = "Blue" } });
        return p;
    }
    [Fact] public void ExactSelectionAndQuantityAreCaptured()
    {
        var text = InquiryProductSelection.Describe(Product(), 8, 3, "en");
        Assert.Contains("BLUE-DESK", text); Assert.Contains("Blue (#8)", text); Assert.Contains("Quantity: 3", text);
    }
    [Theory] [InlineData(null)] [InlineData(9)]
    public void MissingOrForeignVariantIsNotReplacedWithDefault(int? variant) => Assert.Null(InquiryProductSelection.Describe(Product(), variant, 1, "en"));
    [Theory] [InlineData(0)] [InlineData(100)]
    public void InvalidQuantityRejected(int quantity) => Assert.Null(InquiryProductSelection.Describe(Product(), 8, quantity, "en"));
    [Fact] public void InactiveSelectionRejected()
    {
        var p = Product(); p.Variants.Single().IsActive = false;
        Assert.Null(InquiryProductSelection.Describe(p, 8, 1, "en"));
    }
    [Fact] public void PhoneClosureIsStaffOnlyAndRequiresAnOfferedInquiry()
    {
        Assert.Equal(InquiryStatus.Accepted, InquiryService.Next(InquiryStatus.Offered, "Complete", true));
        Assert.Null(InquiryService.Next(InquiryStatus.Offered, "PhoneClose", false));
        Assert.Null(InquiryService.Next(InquiryStatus.New, "PhoneClose", true));
        Assert.Null(InquiryService.Next(InquiryStatus.Closed, "PhoneClose", true));
    }
}
