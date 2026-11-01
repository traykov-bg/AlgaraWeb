using Algara.Data.Models;
using Algara.Data.Repositories;
using Algara.Web.Services.Cart;
using Moq;

namespace Algara.UnitTests.Services;

public class CartServiceTests
{
    private static readonly DateTime Now = new(2026, 10, 28, 12, 0, 0);

    [Fact]
    public async Task Read_RepricesExistingCartFromCurrentPromotionAndFullCurrentSurcharge()
    {
        var product = ColorProduct();
        product.ProductPromotions.Add(new ProductPromotion
        {
            OriginalPrice = 100, PromoPrice = 80,
            Promotion = new Promotion { IsActive = true, StartDate = Now.AddDays(-1), EndDate = Now.AddDays(1) }
        });
        var store = new MemoryCart(new CartEntry(1, 10, 2));
        var service = Service(store, product);
        var first = await service.GetAsync();
        Assert.Equal(210m, first.GoodsSubtotal);
        product.Variants.Single().Surcharge = 30m;
        var second = await service.GetAsync();
        Assert.Equal(220m, second.GoodsSubtotal);
        product.ProductPromotions.Single().Promotion.EndDate = Now.AddSeconds(-1);
        var third = await service.GetAsync();
        Assert.Equal(260m, third.GoodsSubtotal);
        Assert.Equal(0, store.WriteCount);
        Assert.Equal(5, product.Variants.Single().StockQuantity);
    }

    [Fact]
    public async Task Add_MergesOnlyTheSameExactProductVariantAndDoesNotReserveStock()
    {
        var product = ColorProduct();
        var store = new MemoryCart(new CartEntry(1, 10, 1));
        var service = Service(store, product);
        Assert.True((await service.AddAsync(1, 10, 2)).Success);
        Assert.Equal(new CartEntry(1, 10, 3), Assert.Single(store.Read()));
        Assert.Equal(5, product.Variants.Single().StockQuantity);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(100)]
    [InlineData(int.MaxValue)]
    public async Task AddAndUpdate_RejectOutOfRangeQuantitiesWithoutWriting(int quantity)
    {
        var store = new MemoryCart(new CartEntry(1, null, 1));
        var service = Service(store, Basic());
        Assert.Equal(CartLineIssue.InvalidQuantity, (await service.AddAsync(1, null, quantity)).Issue);
        Assert.Equal(CartLineIssue.InvalidQuantity, (await service.UpdateAsync(1, null, quantity)).Issue);
        Assert.Equal(0, store.WriteCount);
    }

    [Fact]
    public async Task Add_RejectsCombinedQuantityAboveLimit()
    {
        var store = new MemoryCart(new CartEntry(1, null, 99));
        var product = Basic(); product.StockQuantity = 999;
        Assert.Equal(CartLineIssue.InvalidQuantity, (await Service(store, product).AddAsync(1, null, 1)).Issue);
        Assert.Equal(0, store.WriteCount);
    }

    [Fact]
    public async Task Add_RejectsMoreThanThirtyDifferentLines()
    {
        var store = new MemoryCart(Enumerable.Range(1, 30).Select(n => new CartEntry(n, null, 1)).ToArray());
        var product = Basic(); product.N = 31;
        Assert.Equal(CartLineIssue.LineLimit, (await Service(store, product).AddAsync(31, null, 1)).Issue);
        Assert.Equal(0, store.WriteCount);
    }

    [Fact]
    public async Task Add_EnforcesStockAgainstTheCombinedQuantity()
    {
        var store = new MemoryCart(new CartEntry(1, null, 4));
        Assert.Equal(CartLineIssue.InsufficientStock, (await Service(store, Basic()).AddAsync(1, null, 2)).Issue);
        Assert.Equal(4, store.Read().Single().Quantity);
    }

    [Fact]
    public async Task Update_CannotInsertAnItemThatIsNotInTheCart()
    {
        var store = new MemoryCart();
        Assert.Equal(CartLineIssue.MissingLine, (await Service(store, Basic()).UpdateAsync(1, null, 1)).Issue);
        Assert.Equal(0, store.WriteCount);
    }

    [Fact]
    public async Task RemovedAndDeactivatedCatalogRowsRemainVisibleAndBlockQuote()
    {
        var product = Basic(); product.IsActive = false;
        var store = new MemoryCart(new CartEntry(1, null, 1), new(2, null, 1));
        var snapshot = await Service(store, product).GetAsync();
        Assert.False(snapshot.CanQuote);
        Assert.Equal(2, snapshot.Lines.Count);
        Assert.Equal(CartLineIssue.InactiveProduct, snapshot.Lines[0].Issue);
        Assert.Equal(CartLineIssue.MissingProduct, snapshot.Lines[1].Issue);
        Assert.Equal(0, store.WriteCount);
    }

    [Fact]
    public async Task Read_QueriesEachProductOnceWhenSeveralVariantsAreStored()
    {
        var product = ColorProduct();
        product.Variants.Add(new ProductVariant
        {
            N = 11, ProductN = 1, Sku = "P1-B", StockQuantity = 5,
            Color = new CatalogColor { N = 3, Name = "Blue" }
        });
        var repository = new Mock<IProductRepository>(MockBehavior.Strict);
        repository.Setup(p => p.GetByNAsync(1)).ReturnsAsync(product);
        var service = new CartService(repository.Object, new MemoryCart(new CartEntry(1, 10, 1), new(1, 11, 2)), new FixedClock());
        var snapshot = await service.GetAsync();
        Assert.True(snapshot.CanQuote);
        Assert.Equal(325m, snapshot.GoodsSubtotal);
        repository.Verify(p => p.GetByNAsync(1), Times.Once);
        repository.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(999)]
    [InlineData(0)]
    public void VariantProductsRequireAnExplicitSelectableOwnedVariant(int? variantN)
        => Assert.Equal(CartLineIssue.InvalidVariant, CartService.Evaluate(new(1, variantN, 1), ColorProduct(), Now).Issue);

    [Fact]
    public void ForeignInactiveAndDeactivatedColorVariantsAreRejected()
    {
        var product = ColorProduct();
        var variant = product.Variants.Single();
        variant.ProductN = 2;
        Assert.Equal(CartLineIssue.InvalidVariant, CartService.Evaluate(new(1, 10, 1), product, Now).Issue);
        variant.ProductN = 1; variant.IsActive = false;
        Assert.Equal(CartLineIssue.InvalidVariant, CartService.Evaluate(new(1, 10, 1), product, Now).Issue);
        variant.IsActive = true; variant.Color.IsActive = false;
        Assert.Equal(CartLineIssue.InvalidVariant, CartService.Evaluate(new(1, 10, 1), product, Now).Issue);
    }

    [Fact]
    public void AVariantCannotBeSuppliedForAProductWithNoOptions()
        => Assert.Equal(CartLineIssue.InvalidVariant, CartService.Evaluate(new(1, 10, 1), Basic(), Now).Issue);

    [Fact]
    public void UnknownVariantStockDoesNotFallBackToProductStock()
    {
        var product = ColorProduct(); product.StockQuantity = 999; product.Variants.Single().StockQuantity = null;
        Assert.Equal(CartLineIssue.UnknownStock, CartService.Evaluate(new(1, 10, 1), product, Now).Issue);
    }

    [Theory]
    [InlineData(null, false, CartLineIssue.UnknownStock, ProductAvailability.Unknown)]
    [InlineData(0, false, CartLineIssue.InquiryRequired, ProductAvailability.Inquiry)]
    [InlineData(1, false, CartLineIssue.InsufficientStock, ProductAvailability.InStock)]
    [InlineData(2, false, CartLineIssue.None, ProductAvailability.InStock)]
    [InlineData(0, true, CartLineIssue.None, ProductAvailability.MadeToOrder)]
    [InlineData(1, true, CartLineIssue.None, ProductAvailability.MadeToOrder)]
    [InlineData(2, true, CartLineIssue.None, ProductAvailability.InStock)]
    public void QuantityAvailabilityDistinguishesManufacturingFromInquiry(int? stock, bool upholstered, CartLineIssue issue, ProductAvailability availability)
    {
        var product = Basic(); product.StockQuantity = stock; product.IsUpholstered = upholstered;
        var line = CartService.Evaluate(new(1, null, 2), product, Now);
        Assert.Equal(issue, line.Issue);
        Assert.Equal(availability, line.Availability);
    }

    [Fact]
    public void EmptyCartCannotBeQuoted()
        => Assert.False(new CartSnapshot().CanQuote);

    [Fact]
    public async Task RemoveCanCleanUpMissingProductsWithoutLookingThemUp()
    {
        var store = new MemoryCart(new CartEntry(99, 55, 1), new(99, 56, 2));
        var service = new CartService(Mock.Of<IProductRepository>(MockBehavior.Strict), store, new FixedClock());
        service.Remove(99, 55);
        Assert.Equal(new CartEntry(99, 56, 2), Assert.Single(store.Read()));
        service.Clear();
        Assert.Empty((await service.GetAsync()).Lines);
    }

    private static Product Basic() => new() { N = 1, Name = "Table", Sku = "P1", Price = 100, StockQuantity = 5 };
    private static Product ColorProduct()
    {
        var product = Basic();
        product.OptionKind = ProductOptionKind.Color;
        product.Variants.Add(new ProductVariant
        {
            N = 10, ProductN = 1, Sku = "P1-W", StockQuantity = 5, Surcharge = 25,
            Color = new CatalogColor { N = 2, Name = "White" }
        });
        return product;
    }
    private static CartService Service(MemoryCart store, Product product)
    {
        var repository = new Mock<IProductRepository>(MockBehavior.Strict);
        repository.Setup(p => p.GetByNAsync(It.IsAny<int>())).ReturnsAsync((int n) => n == product.N ? product : null);
        return new CartService(repository.Object, store, new FixedClock());
    }
    private sealed class FixedClock : TimeProvider
    {
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
        public override DateTimeOffset GetUtcNow() => new(Now, TimeSpan.Zero);
    }
    private sealed class MemoryCart(params CartEntry[] entries) : ICartStore
    {
        private IReadOnlyList<CartEntry> values = entries;
        public int WriteCount { get; private set; }
        public IReadOnlyList<CartEntry> Read() => values;
        public void Write(IReadOnlyList<CartEntry> updated) { values = updated.ToArray(); WriteCount++; }
    }
}
