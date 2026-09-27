using Algara.Data.Models;
using Algara.Data.Repositories;
using Algara.Identity.Data;
using Algara.Web.Controllers;
using Algara.Web.ViewModels.Admin;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Algara.UnitTests.Controllers;

// Direct controller calls exercise promotion business validation and persistence
// decisions, not authorization, antiforgery, model binding or Razor rendering.
public sealed class AdminPromotionControllerTests
{
    [Theory]
    [InlineData("175.25", "12.345", "153.62")]
    [InlineData("999.95", "10", "899.96")]
    public async Task CreatePost_PreservesEnteredPercentageWhenItMatchesRoundedFinalPrice(
        string original, string percent, string final)
    {
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        using var fixture = new Fixture([Product(1, decimal.Parse(original, culture))]);
        var row = Row(1, promo: decimal.Parse(final, culture));
        row.DiscountPercent = decimal.Parse(percent, culture);
        var model = Form(row);
        model.Type = PromotionType.Percent;

        AssertPromotionRedirect(await fixture.Controller.PromotionCreate(model));

        var saved = Assert.Single(fixture.Db.ChangeTracker.Entries<ProductPromotion>()).Entity;
        Assert.Equal(row.DiscountPercent, saved.DiscountPercent);
        Assert.Equal(decimal.Parse(percent, culture), saved.DiscountPercent);
        Assert.Equal(decimal.Parse(final, culture), saved.PromoPrice);
    }

    [Fact]
    public async Task CreateGet_NewRowsStartAtCatalogPriceWithoutDiscount()
    {
        using var fixture = new Fixture([Product(1, 99.49m), Product(2, 150.79m, active: false)]);

        var model = Model(await fixture.Controller.PromotionCreate());

        var row = Assert.Single(model.ProductRows);
        Assert.Equal(1, row.ProductN);
        Assert.False(row.Included);
        Assert.Equal(99.49m, row.CurrentPrice);
        Assert.Equal(99.49m, row.OriginalPrice);
        Assert.Equal(99.49m, row.PromoPrice);
        Assert.Equal(0m, row.DiscountPercent);
        Assert.Equal(0, fixture.Db.SaveCount);
    }

    [Fact]
    public async Task EditGet_ExistingRowsKeepSnapshotAndNewRowsStartWithoutDiscount()
    {
        var promotion = ExistingPromotion(original: 999.95m, promo: 99.49m);
        using var fixture = new Fixture([Product(1, 149.99m), Product(2, 150.79m)], promotion);
        fixture.Promotions.Setup(p => p.GetByNWithProductsAsync(promotion.N)).ReturnsAsync(promotion);

        var model = Model(await fixture.Controller.PromotionEdit(promotion.N));

        var existing = Assert.Single(model.ProductRows, r => r.ProductN == 1);
        Assert.True(existing.Included);
        Assert.Equal(149.99m, existing.CurrentPrice);
        Assert.Equal(999.95m, existing.OriginalPrice);
        Assert.Equal(99.49m, existing.PromoPrice);
        var added = Assert.Single(model.ProductRows, r => r.ProductN == 2);
        Assert.False(added.Included);
        Assert.Equal(150.79m, added.PromoPrice);
        Assert.Equal(0m, added.DiscountPercent);
    }

    [Fact]
    public async Task CreatePost_UsesCatalogPriceAndExactSubmittedCentsIgnoringForgedSnapshotAndPercent()
    {
        using var fixture = new Fixture([Product(1, 999.95m)]);
        var model = Form(Row(1, promo: 99.49m, postedOriginal: 100000m));
        model.ProductRows[0].DiscountPercent = 1m;
        model.ProductRows[0].Note = "  Upholstery included  ";

        var result = await fixture.Controller.PromotionCreate(model);

        AssertPromotionRedirect(result);
        var saved = Assert.Single(fixture.Db.ChangeTracker.Entries<ProductPromotion>()).Entity;
        Assert.Equal(999.95m, saved.OriginalPrice);
        Assert.Equal(99.49m, saved.PromoPrice);
        Assert.Equal(90.051m, saved.DiscountPercent);
        Assert.Equal("Upholstery included", saved.Note);
        Assert.Equal(2, fixture.Db.SaveCount);
    }

    [Fact]
    public async Task CreatePost_PercentageMidpointRoundsAwayFromZeroWithoutChangingPromoPrice()
    {
        using var fixture = new Fixture([Product(1, 2000m)]);

        var result = await fixture.Controller.PromotionCreate(Form(Row(1, promo: 19.99m)));

        AssertPromotionRedirect(result);
        var saved = Assert.Single(fixture.Db.ChangeTracker.Entries<ProductPromotion>()).Entity;
        Assert.Equal(19.99m, saved.PromoPrice);
        Assert.Equal(99.001m, saved.DiscountPercent);
    }

    [Fact]
    public async Task EditPost_PreservesExistingSnapshotAfterCatalogPriceChanges()
    {
        var promotion = ExistingPromotion(original: 999.95m, promo: 99.49m);
        using var fixture = new Fixture([Product(1, 149.99m)], promotion);
        var model = Form(Row(1, promo: 199.49m, postedOriginal: 10000m));

        var result = await fixture.Controller.PromotionEdit(model);

        AssertPromotionRedirect(result);
        var saved = Assert.Single(promotion.ProductPromotions);
        Assert.Equal(999.95m, saved.OriginalPrice);
        Assert.Equal(199.49m, saved.PromoPrice);
        Assert.Equal(80.05m, saved.DiscountPercent);
        Assert.Equal(1, fixture.Db.SaveCount);
    }

    [Fact]
    public async Task EditPost_ExplicitRefreshUsesDatabaseCatalogPriceAndRecomputesDiscount()
    {
        var promotion = ExistingPromotion(original: 999.95m, promo: 99.49m);
        using var fixture = new Fixture([Product(1, 149.99m)], promotion);
        var row = Row(1, promo: 99.49m, postedOriginal: 10000m);
        row.RefreshOriginalPrice = true;

        var result = await fixture.Controller.PromotionEdit(Form(row));

        AssertPromotionRedirect(result);
        var saved = Assert.Single(promotion.ProductPromotions);
        Assert.Equal(149.99m, saved.OriginalPrice);
        Assert.Equal(99.49m, saved.PromoPrice);
        Assert.Equal(33.669m, saved.DiscountPercent);
    }

    [Fact]
    public async Task EditPost_NewProductUsesItsCurrentCatalogPrice()
    {
        var promotion = ExistingPromotion(original: 999.95m, promo: 99.49m);
        using var fixture = new Fixture([Product(1, 149.99m), Product(2, 150.79m)], promotion);
        var model = Form(Row(1, promo: 99.49m), Row(2, promo: 129.79m, postedOriginal: 10000m));

        var result = await fixture.Controller.PromotionEdit(model);

        AssertPromotionRedirect(result);
        var saved = Assert.Single(fixture.Db.ChangeTracker.Entries<ProductPromotion>(), e => e.Entity.ProductN == 2).Entity;
        Assert.Equal(150.79m, saved.OriginalPrice);
        Assert.Equal(129.79m, saved.PromoPrice);
        Assert.Equal(13.927m, saved.DiscountPercent);
    }

    public static TheoryData<bool, decimal> InvalidPrices => new()
    {
        { false, 0m }, { true, 0m },
        { false, -0.01m }, { true, -0.01m },
        { false, 100m }, { true, 100m },
        { false, 100.01m }, { true, 100.01m },
        { false, 99.499m }, { true, 99.499m },
    };

    [Theory]
    [MemberData(nameof(InvalidPrices))]
    public async Task Post_InvalidPriceReturnsValidationWithoutWriting(bool edit, decimal promoPrice)
    {
        var promotion = ExistingPromotion(original: 100m, promo: 80m);
        using var fixture = new Fixture([Product(1, 100m)], edit ? promotion : null);
        var model = Form(Row(1, promo: promoPrice, postedOriginal: 100000m));

        var result = await fixture.Post(model, edit);

        Assert.Same(model, Model(result));
        Assert.False(fixture.Controller.ModelState.IsValid);
        Assert.Contains(fixture.Controller.ModelState, e => e.Key.EndsWith(".PromoPrice") && e.Value!.Errors.Count > 0);
        Assert.Equal(0, fixture.Db.SaveCount);
        Assert.Equal(80m, Assert.Single(promotion.ProductPromotions).PromoPrice);
    }

    [Fact]
    public async Task EditPost_RefreshCannotTurnAStoredDiscountIntoMarkup()
    {
        var promotion = ExistingPromotion(original: 999.95m, promo: 199.49m);
        using var fixture = new Fixture([Product(1, 149.99m)], promotion);
        var row = Row(1, promo: 199.49m, postedOriginal: 10000m);
        row.RefreshOriginalPrice = true;

        var result = await fixture.Controller.PromotionEdit(Form(row));

        var model = Model(result);
        Assert.False(fixture.Controller.ModelState.IsValid);
        Assert.Equal(149.99m, Assert.Single(model.ProductRows).OriginalPrice);
        Assert.Equal(999.95m, Assert.Single(promotion.ProductPromotions).OriginalPrice);
        Assert.Equal(0, fixture.Db.SaveCount);
    }

    [Theory]
    [InlineData(false, "unknown")]
    [InlineData(true, "unknown")]
    [InlineData(false, "inactive")]
    [InlineData(true, "inactive")]
    [InlineData(false, "duplicate")]
    [InlineData(true, "duplicate")]
    public async Task Post_InvalidSelectedProductReturnsValidationWithoutWriting(bool edit, string kind)
    {
        var promotion = ExistingPromotion(original: 100m, promo: 80m);
        using var fixture = new Fixture([Product(1, 100m), Product(2, 120m, active: false)], edit ? promotion : null);
        var rows = kind switch
        {
            "unknown" => new[] { Row(999, promo: 50m) },
            "inactive" => new[] { Row(2, promo: 50m) },
            _ => new[] { Row(1, promo: 50m), Row(1, promo: 60m) },
        };

        var result = await fixture.Post(Form(rows), edit);

        Assert.IsType<ViewResult>(result);
        Assert.False(fixture.Controller.ModelState.IsValid);
        Assert.Equal(0, fixture.Db.SaveCount);
        Assert.Equal(80m, Assert.Single(promotion.ProductPromotions).PromoPrice);
    }

    [Fact]
    public async Task CreatePost_InvalidModelReloadsProductInfoAndAuthoritativePrice()
    {
        using var fixture = new Fixture([Product(1, 99.49m), Product(2, 150.79m)]);
        var model = Form(Row(1, promo: 79.49m, postedOriginal: 999m));
        fixture.Controller.ModelState.AddModelError(nameof(model.Name), "Name is required.");

        var result = await fixture.Controller.PromotionCreate(model);

        var returned = Model(result);
        var posted = Assert.Single(returned.ProductRows, r => r.ProductN == 1);
        Assert.Equal("Product 1", posted.ProductName);
        Assert.Equal(99.49m, posted.CurrentPrice);
        Assert.Equal(99.49m, posted.OriginalPrice);
        Assert.Equal(79.49m, posted.PromoPrice);
        var missing = Assert.Single(returned.ProductRows, r => r.ProductN == 2);
        Assert.False(missing.Included);
        Assert.Equal(150.79m, missing.PromoPrice);
        Assert.Equal(0, fixture.Db.SaveCount);
    }

    private static Product Product(int n, decimal price, bool active = true)
        => new() { N = n, Name = $"Product {n}", Price = price, IsActive = active };

    private static Promotion ExistingPromotion(decimal original, decimal promo)
    {
        var promotion = new Promotion
        {
            N = 7, Name = "Existing", StartDate = new DateTime(2026, 9, 1), EndDate = new DateTime(2026, 10, 1),
        };
        promotion.ProductPromotions.Add(new ProductPromotion
        {
            ProductN = 1, PromotionN = promotion.N, Promotion = promotion,
            OriginalPrice = original, PromoPrice = promo, DiscountPercent = 20m,
        });
        return promotion;
    }

    private static AdminPromotionProductRowViewModel Row(int n, decimal promo, decimal postedOriginal = 100m)
        => new() { ProductN = n, Included = true, OriginalPrice = postedOriginal, PromoPrice = promo, DiscountPercent = 1m };

    private static AdminPromotionFormViewModel Form(params AdminPromotionProductRowViewModel[] rows)
        => new()
        {
            N = 7, Name = "September offers", StartDate = new DateTime(2026, 9, 1),
            EndDate = new DateTime(2026, 10, 1), ProductRows = rows.ToList(),
        };

    private static AdminPromotionFormViewModel Model(IActionResult result)
        => Assert.IsType<AdminPromotionFormViewModel>(Assert.IsType<ViewResult>(result).Model);

    private static void AssertPromotionRedirect(IActionResult result)
        => Assert.Equal(nameof(AdminController.Promotions), Assert.IsType<RedirectToActionResult>(result).ActionName);

    private sealed class Fixture : IDisposable
    {
        private readonly IdentityDbContext _identityDb = new(new DbContextOptions<IdentityDbContext>());
        public PromotionTestContext Db { get; }
        public Mock<IPromotionRepository> Promotions { get; } = new(MockBehavior.Strict);
        public AdminController Controller { get; }

        public Fixture(Product[] products, Promotion? promotion = null)
        {
            Db = new PromotionTestContext(products, promotion == null ? [] : [promotion]);
            var http = new DefaultHttpContext();
            Controller = new AdminController(Db, _identityDb,
                Mock.Of<IProductRepository>(MockBehavior.Strict), Mock.Of<ICategoryRepository>(MockBehavior.Strict),
                Mock.Of<IOrderRepository>(MockBehavior.Strict), Mock.Of<IHeroSlideRepository>(MockBehavior.Strict),
                Promotions.Object, Mock.Of<IUserService>(MockBehavior.Strict), NullLogger<AdminController>.Instance,
                Mock.Of<IWebHostEnvironment>(MockBehavior.Strict))
            {
                ControllerContext = new ControllerContext { HttpContext = http },
                TempData = new TempDataDictionary(http, Mock.Of<ITempDataProvider>()),
            };
        }

        public Task<IActionResult> Post(AdminPromotionFormViewModel model, bool edit)
            => edit ? Controller.PromotionEdit(model) : Controller.PromotionCreate(model);

        public void Dispose()
        {
            Db.Dispose();
            _identityDb.Dispose();
        }
    }
}
