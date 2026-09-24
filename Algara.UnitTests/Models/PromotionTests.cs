using Algara.Data.Models;

namespace Algara.UnitTests.Models;

public class PromotionTests
{
    [Theory]
    [InlineData(PromotionType.Percent)]
    [InlineData(PromotionType.Amount)]
    public void NoProducts_HasNoDiscountRange(PromotionType type)
    {
        var promotion = new Promotion { Type = type };

        Assert.Empty(promotion.GetDiscountRangeLabel());
    }

    [Theory]
    [InlineData("en-US", "-12.5%")]
    [InlineData("bg-BG", "-12,5%")]
    public void EqualPercentages_ShowOnePercentageEvenWhenMoneyDiscountsDiffer(
        string cultureName, string expectedLabel)
    {
        using var culture = new CultureScope(cultureName);
        var promotion = new Promotion
        {
            Type = PromotionType.Percent,
            ProductPromotions =
            [
                new() { OriginalPrice = 200m, PromoPrice = 175m, DiscountPercent = 12.5m },
                new() { OriginalPrice = 400m, PromoPrice = 350m, DiscountPercent = 12.5m }
            ]
        };

        Assert.Equal(expectedLabel, promotion.GetDiscountRangeLabel());
    }

    [Theory]
    [InlineData("en-US", "-5.125% – -30%")]
    [InlineData("bg-BG", "-5,125% – -30%")]
    public void DifferentPercentages_ShowSmallestAndLargestRegardlessOfRowOrder(
        string cultureName, string expectedLabel)
    {
        using var culture = new CultureScope(cultureName);
        var promotion = new Promotion
        {
            Type = PromotionType.Percent,
            ProductPromotions =
            [
                new() { DiscountPercent = 30m },
                new() { DiscountPercent = 5.125m },
                new() { DiscountPercent = 20m }
            ]
        };

        Assert.Equal(expectedLabel, promotion.GetDiscountRangeLabel());
    }

    [Theory]
    [InlineData("en-US", "-20.50 €")]
    [InlineData("bg-BG", "-20,50 €")]
    public void EqualAmounts_ShowOneAmountEvenWhenPercentageDiscountsDiffer(
        string cultureName, string expectedLabel)
    {
        using var culture = new CultureScope(cultureName);
        var promotion = new Promotion
        {
            Type = PromotionType.Amount,
            ProductPromotions =
            [
                new() { OriginalPrice = 100m, PromoPrice = 79.50m, DiscountPercent = 20.5m },
                new() { OriginalPrice = 200m, PromoPrice = 179.50m, DiscountPercent = 10.25m }
            ]
        };

        Assert.Equal(expectedLabel, promotion.GetDiscountRangeLabel());
    }

    [Theory]
    [InlineData("en-US", "-20.50 € – -100 €")]
    [InlineData("bg-BG", "-20,50 € – -100 €")]
    public void DifferentAmounts_ShowRangeFromRecordedPriceDifferences(
        string cultureName, string expectedLabel)
    {
        using var culture = new CultureScope(cultureName);
        var promotion = new Promotion
        {
            Type = PromotionType.Amount,
            ProductPromotions =
            [
                new() { OriginalPrice = 500m, PromoPrice = 400m },
                new() { OriginalPrice = 200m, PromoPrice = 179.50m },
                new() { OriginalPrice = 100m, PromoPrice = 50m }
            ]
        };

        Assert.Equal(expectedLabel, promotion.GetDiscountRangeLabel());
    }
}
