using System.Globalization;
using Algara.Data.Models;

namespace Algara.UnitTests.Models;

public class ProductPromotionTests
{
    [Theory]
    [InlineData("en-US", "30", "-30%")]
    [InlineData("bg-BG", "30", "-30%")]
    [InlineData("en-US", "12.50", "-12.5%")]
    [InlineData("bg-BG", "12.50", "-12,5%")]
    [InlineData("en-US", "5.125", "-5.125%")]
    [InlineData("bg-BG", "5.125", "-5,125%")]
    public void PercentageLabel_PreservesRecordedPrecisionAndUsesCurrentCulture(
        string cultureName, string recordedPercentage, string expectedLabel)
    {
        using var culture = new CultureScope(cultureName);
        var row = new ProductPromotion
        {
            Promotion = new Promotion { Type = PromotionType.Percent },
            OriginalPrice = 199.99m,
            PromoPrice = 174.99m,
            DiscountPercent = decimal.Parse(recordedPercentage, CultureInfo.InvariantCulture)
        };

        Assert.Equal(expectedLabel, row.GetDiscountLabel());
    }

    [Theory]
    [InlineData("en-US", "180.00", "-20 €")]
    [InlineData("bg-BG", "180.00", "-20 €")]
    [InlineData("en-US", "179.50", "-20.50 €")]
    [InlineData("bg-BG", "179.50", "-20,50 €")]
    public void AmountLabel_UsesSnapshotDifferenceAndCurrentCulture(
        string cultureName, string finalPrice, string expectedLabel)
    {
        using var culture = new CultureScope(cultureName);
        var row = new ProductPromotion
        {
            Promotion = new Promotion { Type = PromotionType.Amount },
            Product = new Product { Price = 300m },
            OriginalPrice = 200m,
            PromoPrice = decimal.Parse(finalPrice, CultureInfo.InvariantCulture),
            DiscountPercent = 10m
        };

        Assert.Equal(expectedLabel, row.GetDiscountLabel());
    }

    [Fact]
    public void DiscountAmount_PreservesCentsAndIgnoresLaterProductPrice()
    {
        var row = new ProductPromotion
        {
            Product = new Product { Price = 499.99m },
            OriginalPrice = 199.99m,
            PromoPrice = 169.50m
        };

        Assert.Equal(30.49m, row.DiscountAmount);
    }
}
