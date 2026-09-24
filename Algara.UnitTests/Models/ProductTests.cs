using Algara.Data.Models;

namespace Algara.UnitTests.Models;

public class ProductTests
{
    private static readonly DateTime Now = new(2026, 9, 24, 12, 0, 0);

    [Fact]
    public void WithoutPromotions_UsesRegularPriceAndHasNoDiscount()
    {
        var product = new Product { Price = 129.95m };

        Assert.Null(product.GetActivePromotion(Now));
        Assert.Null(product.GetActiveDiscount(Now));
        Assert.Equal(129.95m, product.GetDiscountedPrice(Now));
        Assert.Empty(product.GetDiscountLabel(Now));
    }

    [Theory]
    [InlineData(-1L, false)]
    [InlineData(0L, true)]
    [InlineData(300_000_000L, true)]
    [InlineData(600_000_000L, true)]
    [InlineData(600_000_001L, false)]
    public void PromotionPeriod_IncludesBothBoundaries(long ticksAfterStart, bool expectedActive)
    {
        var product = new Product { Price = 100m };
        var promotion = CreatePromotion(100m, 80m, 20m);
        promotion.Promotion.StartDate = Now;
        promotion.Promotion.EndDate = Now.AddMinutes(1);
        product.ProductPromotions.Add(promotion);
        var evaluationTime = Now.AddTicks(ticksAfterStart);

        Assert.Same(expectedActive ? promotion : null, product.GetActivePromotion(evaluationTime));
        Assert.Equal(expectedActive ? 80m : 100m, product.GetDiscountedPrice(evaluationTime));
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("future")]
    [InlineData("expired")]
    [InlineData("missing")]
    public void UnavailablePromotion_DoesNotGiveADiscount(string unavailableReason)
    {
        var product = new Product { Price = 200m };
        var unavailable = CreatePromotion(200m, 10m, 95m);
        switch (unavailableReason)
        {
            case "disabled":
                unavailable.Promotion.IsActive = false;
                break;
            case "future":
                unavailable.Promotion.StartDate = Now.AddTicks(1);
                break;
            case "expired":
                unavailable.Promotion.EndDate = Now.AddTicks(-1);
                break;
            case "missing":
                unavailable.Promotion = null!;
                break;
        }
        product.ProductPromotions.Add(unavailable);

        Assert.Null(product.GetActivePromotion(Now));
        Assert.Null(product.GetActiveDiscount(Now));
        Assert.Equal(200m, product.GetDiscountedPrice(Now));
        Assert.Empty(product.GetDiscountLabel(Now));

        var available = CreatePromotion(200m, 180m, 10m);
        product.ProductPromotions.Add(available);

        Assert.Same(available, product.GetActivePromotion(Now));
        Assert.Equal(180m, product.GetDiscountedPrice(Now));
    }

    [Fact]
    public void CompetingPromotions_SelectLowestFinalPriceAndItsOwnDiscountLabel()
    {
        var product = new Product { Price = 500m };
        var largestPercentage = CreatePromotion(300m, 240m, 20m);
        var lowestFinalPrice = CreatePromotion(200m, 190m, 5m);
        lowestFinalPrice.Promotion.Type = PromotionType.Amount;
        product.ProductPromotions = [largestPercentage, lowestFinalPrice];

        Assert.Same(lowestFinalPrice, product.GetActivePromotion(Now));
        Assert.Equal(190m, product.GetDiscountedPrice(Now));
        Assert.Equal(5m, product.GetActiveDiscount(Now));
        Assert.Equal("-10 €", product.GetDiscountLabel(Now));
    }

    [Fact]
    public void RegularPriceChange_DoesNotRecalculateRecordedPromotionPrice()
    {
        var product = new Product { Price = 199.99m };
        var promotion = CreatePromotion(199.99m, 174.99m, 12.5m);
        product.ProductPromotions.Add(promotion);
        product.Price = 299.99m;

        Assert.Equal(174.99m, product.GetDiscountedPrice(Now));
        Assert.Equal(12.5m, product.GetActiveDiscount(Now));
        Assert.Equal(199.99m, promotion.OriginalPrice);
        Assert.Equal(25m, promotion.DiscountAmount);
        Assert.Equal(299.99m, product.GetDiscountedPrice(Now.AddDays(2)));
    }

    private static ProductPromotion CreatePromotion(decimal originalPrice, decimal promoPrice, decimal discountPercent)
    {
        return new ProductPromotion
        {
            OriginalPrice = originalPrice,
            PromoPrice = promoPrice,
            DiscountPercent = discountPercent,
            Promotion = new Promotion
            {
                IsActive = true,
                StartDate = Now.AddDays(-1),
                EndDate = Now.AddDays(1)
            }
        };
    }
}
