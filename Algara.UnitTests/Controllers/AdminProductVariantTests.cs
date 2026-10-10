using System.Globalization;
using System.Security.Claims;
using Algara.Data.Models;
using Algara.Data.Repositories;
using Algara.Identity.Data;
using Algara.Web.Controllers;
using Algara.Web.Services;
using Algara.Web.ViewModels.Admin;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Binders;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using Moq;

namespace Algara.UnitTests.Controllers;

// Controller decisions use in-memory rows and mocked saves. SQL constraints and
// actual HTTP authorization/concurrency remain separate integration checks.
public class AdminProductVariantTests
{
    [Fact]
    public async Task CreateColorProductPersistsSeparateStockAndSurcharges()
    {
        using var fixture = new Fixture();
        var form = ColorForm();
        form.StockQuantity = 999;
        form.Variants[1].Sku = " v-red ";
        Redirect(await fixture.Controller.ProductCreate(form));
        var saved = Assert.Single(fixture.Saved);
        Assert.Equal(ProductOptionKind.Color, saved.OptionKind);
        Assert.Null(saved.StockQuantity);
        Assert.Equal(new int?[] { 3, 0 }, saved.Variants.Select(v => v.StockQuantity));
        Assert.Equal(new[] { 0m, 12.34m }, saved.Variants.Select(v => v.Surcharge));
        Assert.Equal("V-RED", saved.Variants.Last().Sku);
        Assert.True(saved.Variants.First().IsDefault);
    }

    [Fact]
    public async Task CreateFabricProductStoresOneSurchargeSharedByAllItsColors()
    {
        using var fixture = new Fixture();
        var form = FabricForm();
        Redirect(await fixture.Controller.ProductCreate(form));
        var saved = Assert.Single(fixture.Saved);
        Assert.True(saved.IsUpholstered);
        Assert.Equal(3, saved.Variants.Count);
        Assert.All(saved.Variants, v => Assert.Equal(0m, v.Surcharge));
        Assert.Equal(80m, saved.FabricSurcharges.Single(s => s.FabricN == 6).Surcharge);
        Assert.Equal(0m, saved.FabricSurcharges.Single(s => s.FabricN == 5).Surcharge);
    }

    [Fact]
    public async Task LegacyProductWithoutOptionsKeepsUnknownQuantity()
    {
        var existing = Existing();
        using var fixture = new Fixture(existing);
        var form = BaseForm(existing);
        Redirect(await fixture.Controller.ProductEdit(form));
        Assert.Null(existing.StockQuantity);
        Assert.Empty(existing.Variants);
        Assert.Equal(ProductOptionKind.None, existing.OptionKind);
        Assert.Equal("SAVED-01", existing.Sku);
    }

    [Theory]
    [InlineData(999)]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task UnknownColorCannotCreateVariant(int colorN)
    {
        using var fixture = new Fixture();
        var form = ColorForm();
        form.Variants[1].CatalogColorN = colorN;
        await InvalidCreate(fixture, form, "Variants[1].CatalogColorN");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InactiveCatalogChoiceCannotBecomeActiveVariant(bool fabric)
    {
        using var fixture = new Fixture();
        var form = fabric ? FabricForm() : ColorForm();
        if (fabric) fixture.Fabrics.Single(f => f.N == 6).IsActive = false;
        else fixture.Colors.Single(c => c.N == 2).IsActive = false;
        await InvalidCreate(fixture, form, "Variants[1].CatalogColorN");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WrongColorKindIsRejected(bool fabricMode)
    {
        using var fixture = new Fixture();
        var form = fabricMode ? FabricForm() : ColorForm();
        form.Variants[1].CatalogColorN = fabricMode ? 2 : 20;
        await InvalidCreate(fixture, form, "Variants[1].CatalogColorN");
    }

    [Fact]
    public async Task FabricChoiceAutomaticallySetsManufacturingClassification()
    {
        using var fixture = new Fixture();
        var form = FabricForm();
        Redirect(await fixture.Controller.ProductCreate(form));
        Assert.True(Assert.Single(fixture.Saved).IsUpholstered);
    }

    [Fact]
    public async Task FabricColorsCannotAddIndividualSurcharge()
    {
        using var fixture = new Fixture();
        var form = FabricForm();
        form.Variants[1].Surcharge = 1m;
        await InvalidCreate(fixture, form, "Variants[1].Surcharge");
    }

    [Fact]
    public async Task UsedFabricMustHaveExplicitSharedPriceEvenWhenZero()
    {
        using var fixture = new Fixture();
        var form = FabricForm();
        form.FabricSurcharges.RemoveAt(1);
        await InvalidCreate(fixture, form, "Variants[1].Surcharge");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BaseChoiceMustHaveZeroSurcharge(bool fabric)
    {
        using var fixture = new Fixture();
        var form = fabric ? FabricForm() : ColorForm();
        if (fabric) form.FabricSurcharges[0].Surcharge = 1;
        else form.Variants[0].Surcharge = 1;
        await InvalidCreate(fixture, form, nameof(form.Variants));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task ExactlyOneActiveDefaultIsRequired(int count)
    {
        using var fixture = new Fixture();
        var form = ColorForm();
        form.Variants[0].IsDefault = count != 0;
        form.Variants[1].IsDefault = count == 2;
        await InvalidCreate(fixture, form, nameof(form.Variants));
    }

    [Fact]
    public async Task DuplicateColorsAndNormalizedCodesAreRejected()
    {
        using var fixture = new Fixture();
        var form = ColorForm();
        form.Variants[0].Sku = "same";
        form.Variants[1].Sku = " SAME ";
        form.Variants[1].CatalogColorN = form.Variants[0].CatalogColorN;
        await InvalidCreate(fixture, form, nameof(form.Variants));
        Error(fixture, "Variants[1].Sku");
    }

    [Fact]
    public async Task DuplicateFabricSurchargesAreRejected()
    {
        using var fixture = new Fixture();
        var form = FabricForm();
        form.FabricSurcharges.Add(new() { FabricN = 6, Surcharge = 1 });
        await InvalidCreate(fixture, form, nameof(form.FabricSurcharges));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExistingProductOrVariantCodeCannotBeReused(bool variantCode)
    {
        var other = Existing();
        other.Variants.Add(new() { N = 100, Sku = "USED-VARIANT" });
        using var fixture = new Fixture(other);
        var form = ColorForm();
        form.Variants[1].Sku = variantCode ? "USED-VARIANT" : "SAVED-01";
        await InvalidCreate(fixture, form, nameof(form.Variants));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1000001)]
    public async Task InvalidStockCannotBeSaved(int quantity)
    {
        using var fixture = new Fixture();
        var form = ColorForm();
        form.StockQuantity = quantity;
        form.Variants[1].StockQuantity = quantity;
        await InvalidCreate(fixture, form, "Variants[1].StockQuantity");
        Error(fixture, nameof(form.StockQuantity));
    }

    [Theory]
    [InlineData("-0.01")]
    [InlineData("0.001")]
    [InlineData("10000000")]
    public async Task InvalidSurchargeCannotBeSavedForColorOrFabric(string input)
    {
        var value = decimal.Parse(input, CultureInfo.InvariantCulture);
        using (var fixture = new Fixture())
        {
            var form = ColorForm();
            form.Variants[1].Surcharge = value;
            await InvalidCreate(fixture, form, "Variants[1].Surcharge");
        }
        using (var fixture = new Fixture())
        {
            var form = FabricForm();
            form.FabricSurcharges[1].Surcharge = value;
            await InvalidCreate(fixture, form, nameof(form.FabricSurcharges));
        }
    }

    [Theory]
    [InlineData("1.5")]
    [InlineData("1,5")]
    public async Task FractionalStockIsRejectedByRealIntegerModelBinder(string input)
    {
        var http = new DefaultHttpContext();
        var action = new ActionContext(http, new RouteData(), new ControllerActionDescriptor());
        var values = new FormValueProvider(BindingSource.Form,
            new FormCollection(new Dictionary<string, StringValues> { ["Variants[0].StockQuantity"] = input }), CultureInfo.GetCultureInfo("bg-BG"));
        var metadata = new EmptyModelMetadataProvider().GetMetadataForType(typeof(int?));
        var binding = DefaultModelBindingContext.CreateBindingContext(action, values, metadata, null, "Variants[0].StockQuantity");
        await new SimpleTypeModelBinder(typeof(int?), NullLoggerFactory.Instance).BindModelAsync(binding);
        Assert.False(binding.Result.IsModelSet);
        Assert.False(binding.ModelState.IsValid);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ForeignVariantIdOrChangedColorDoesNotMutateExistingProduct(bool changedColor)
    {
        var existing = VariantProduct();
        using var fixture = new Fixture(existing);
        var form = await EditForm(fixture, existing.N);
        if (changedColor) form.Variants[0].CatalogColorN = 2;
        else form.Variants[0].N = 999;
        Assert.IsType<ViewResult>(await fixture.Controller.ProductEdit(form));
        Error(fixture, "Variants[0].N");
        Assert.Empty(fixture.Saved);
        Assert.Equal(1, existing.Variants.First().CatalogColorN);
        Assert.Equal("Original", existing.Name);
    }

    [Fact]
    public async Task StaleRowVersionCannotOverwriteStockOrPrices()
    {
        var existing = VariantProduct();
        using var fixture = new Fixture(existing);
        var form = await EditForm(fixture, existing.N);
        form.RowVersion = Convert.ToBase64String([9, 9, 9]);
        form.Variants[0].StockQuantity = 500;
        form.Price = 900;
        Assert.IsType<ViewResult>(await fixture.Controller.ProductEdit(form));
        Error(fixture, string.Empty);
        Assert.Empty(fixture.Saved);
        Assert.Equal(3, existing.Variants.First().StockQuantity);
        Assert.Equal(75m, existing.Price);
    }

    [Fact]
    public async Task DeactivatedAndOmittedVariantsKeepIdentityAndCanBeReactivated()
    {
        var existing = VariantProduct();
        var second = existing.Variants.Last();
        using var fixture = new Fixture(existing);
        var form = await EditForm(fixture, existing.N);
        form.Variants.RemoveAt(1);
        Redirect(await fixture.Controller.ProductEdit(form));
        Assert.Equal(2, existing.Variants.Count);
        Assert.Same(second, existing.Variants.Last());
        Assert.False(second.IsActive);
        Assert.Equal("V-SECOND", second.Sku);
        Assert.Equal(12.34m, second.Surcharge);
        form = await EditForm(fixture, existing.N);
        form.Variants.Single(v => v.N == second.N).IsActive = true;
        Redirect(await fixture.Controller.ProductEdit(form));
        Assert.True(second.IsActive);
        Assert.Equal(102, second.N);
        Assert.Equal(2, fixture.Saved.Count);
    }

    [Fact]
    public async Task ExistingSelectionCannotBeRecreatedWithNewId()
    {
        var existing = VariantProduct();
        using var fixture = new Fixture(existing);
        var form = await EditForm(fixture, existing.N);
        form.Variants[0].N = 0;
        Assert.IsType<ViewResult>(await fixture.Controller.ProductEdit(form));
        Error(fixture, "Variants[0].N");
        Assert.Empty(fixture.Saved);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ForeignOrDeletedVariantImageIsRejectedEvenIfPostedAsGalleryMember(bool forgedGallery)
    {
        var existing = VariantProduct();
        existing.Images.Add(new() { N = 201, ProductN = existing.N, ImageUrl = "/own.jpg" });
        using var fixture = new Fixture(existing);
        var form = await EditForm(fixture, existing.N);
        form.Variants[0].ProductImageN = 999;
        if (forgedGallery) form.Images.Add(new() { N = 999, ImageUrl = "/foreign.jpg" });
        Assert.IsType<ViewResult>(await fixture.Controller.ProductEdit(form));
        Error(fixture, forgedGallery ? nameof(form.Images) : "Variants[0].ProductImageN");
        Assert.Empty(fixture.Saved);
        Assert.Null(existing.Variants.First().ProductImageN);
        Assert.Single(existing.Images);
    }

    [Fact]
    public async Task GalleryDeletionClearsExactVariantPhotoAndRejectsStaleDeletion()
    {
        var existing = VariantProduct();
        var image = new ProductImage { N = 201, ProductN = existing.N, ImageUrl = "/exact.jpg" };
        existing.Images.Add(image);
        existing.Variants.First().ProductImageN = image.N;
        existing.Variants.First().Image = image;
        using var fixture = new Fixture(existing);
        var rejected = Assert.IsType<JsonResult>(await fixture.Controller.ProductImageDelete(existing.N, null, image.N, null, "stale"));
        Assert.Equal(409, rejected.StatusCode);
        Assert.Same(image, existing.Variants.First().Image);
        Assert.Empty(fixture.Saved);
        Assert.IsType<JsonResult>(await fixture.Controller.ProductImageDelete(existing.N, null, image.N, null, Convert.ToBase64String(existing.RowVersion)));
        Assert.Null(existing.Variants.First().Image);
        Assert.Null(existing.Variants.First().ProductImageN);
        Assert.Empty(existing.Images);
        Assert.Single(fixture.Saved);
    }

    [Fact]
    public async Task GalleryPhotoCanBeAssignedWithoutChangingGalleryMain()
    {
        var existing = VariantProduct();
        existing.Images = [new() { N = 201, ProductN = 7, ImageUrl = "/base.jpg", DisplayOrder = 0 }, new() { N = 202, ProductN = 7, ImageUrl = "/exact.jpg", DisplayOrder = 1 }];
        using var fixture = new Fixture(existing);
        var form = await EditForm(fixture, existing.N);
        form.Variants[1].ProductImageN = 202;
        Redirect(await fixture.Controller.ProductEdit(form));
        Assert.Equal(202, existing.Variants.Last().ProductImageN);
        Assert.Same(existing.Images.Last(), existing.Variants.Last().Image);
        Assert.Equal("/base.jpg", existing.GetMainImageUrl());
    }

    [Theory]
    [InlineData(ProductOptionKind.None)]
    [InlineData(ProductOptionKind.Color)]
    public async Task EditingLegacyNonFabricProductPreservesItsManufacturingClassification(ProductOptionKind kind)
    {
        var existing = kind == ProductOptionKind.None ? Existing() : VariantProduct();
        existing.IsUpholstered = true;
        using var fixture = new Fixture(existing);
        var form = await EditForm(fixture, existing.N);
        Redirect(await fixture.Controller.ProductEdit(form));
        Assert.True(existing.IsUpholstered);
    }

    [Fact]
    public async Task RemovingFabricGroupKeepsVariantIdentityStockAndPhotoForLaterReaddition()
    {
        var existing = Existing();
        existing.OptionKind = ProductOptionKind.Fabric;
        existing.IsUpholstered = true;
        existing.Images = [new() { N = 201, ProductN = 7, ImageUrl = "/variant.jpg" }];
        existing.Variants = [
            new() { N = 101, ProductN = 7, CatalogColorN = 10, Sku = "V-BASE", IsDefault = true },
            new() { N = 102, ProductN = 7, CatalogColorN = 20, Sku = "V-BLUE", StockQuantity = 4, ProductImageN = 201, Image = existing.Images.Single() },
            new() { N = 103, ProductN = 7, CatalogColorN = 21, Sku = "V-RED", StockQuantity = 0, IsActive = false }
        ];
        existing.FabricSurcharges = [new() { ProductN = 7, FabricN = 5 }, new() { ProductN = 7, FabricN = 6, Surcharge = 80m }];
        var removedVariant = existing.Variants.Single(v => v.N == 102);
        using var fixture = new Fixture(existing);
        var form = await EditForm(fixture, existing.N);
        form.FabricSurcharges.RemoveAll(s => s.FabricN == 6);
        form.Variants.RemoveAll(v => v.CatalogColorN is 20 or 21);
        Redirect(await fixture.Controller.ProductEdit(form));
        Assert.Equal(5, Assert.Single(existing.FabricSurcharges).FabricN);
        Assert.Equal(3, existing.Variants.Count);
        Assert.Same(removedVariant, existing.Variants.Single(v => v.N == 102));
        Assert.False(removedVariant.IsActive);
        Assert.Equal(4, removedVariant.StockQuantity);
        Assert.Equal(201, removedVariant.ProductImageN);
        Assert.Equal("V-BLUE", removedVariant.Sku);

        form = await EditForm(fixture, existing.N);
        Assert.Equal(5, Assert.Single(form.FabricSurcharges).FabricN);
        Assert.False(form.Variants.Single(v => v.N == 102).IsActive);
        form.FabricSurcharges.Add(new() { FabricN = 6, Surcharge = 95.50m });
        form.Variants.Single(v => v.N == 102).IsActive = true;
        Redirect(await fixture.Controller.ProductEdit(form));
        Assert.True(removedVariant.IsActive);
        Assert.Equal(4, removedVariant.StockQuantity);
        Assert.Equal(201, removedVariant.ProductImageN);
        Assert.Equal(95.50m, existing.FabricSurcharges.Single(s => s.FabricN == 6).Surcharge);
    }

    [Fact]
    public async Task RemovingLastFabricCanSaveAsProductWithoutVariants()
    {
        var existing = Existing();
        existing.OptionKind = ProductOptionKind.Fabric;
        existing.IsUpholstered = true;
        existing.Variants = [new() { N = 101, ProductN = 7, CatalogColorN = 10, Sku = "V-BASE", IsDefault = true }];
        existing.FabricSurcharges = [new() { ProductN = 7, FabricN = 5 }];
        using var fixture = new Fixture(existing);
        var form = await EditForm(fixture, existing.N);
        form.OptionKind = ProductOptionKind.None;
        form.Variants.Clear();
        form.FabricSurcharges.Clear();
        Redirect(await fixture.Controller.ProductEdit(form));
        Assert.Empty(existing.FabricSurcharges);
        Assert.False(Assert.Single(existing.Variants).IsActive);
        Assert.False(existing.IsUpholstered);
        Assert.Equal(ProductOptionKind.None, existing.OptionKind);
    }
    [Fact]
    public async Task InvalidFormRetainsOmittedFabricRowsForReaddingWithoutDuplicateIdentities()
    {
        var existing = Existing();
        existing.OptionKind = ProductOptionKind.Fabric;
        existing.IsUpholstered = true;
        existing.Images = [new() { N = 201, ProductN = 7, ImageUrl = "/variant.jpg" }];
        existing.Variants = [
            new() { N = 101, ProductN = 7, CatalogColorN = 10, Sku = "V-BASE", IsDefault = true },
            new() { N = 102, ProductN = 7, CatalogColorN = 20, Sku = "V-BLUE", StockQuantity = 4, ProductImageN = 201, Image = existing.Images.Single() }
        ];
        existing.FabricSurcharges = [new() { ProductN = 7, FabricN = 5 }, new() { ProductN = 7, FabricN = 6, Surcharge = 80m }];
        using var fixture = new Fixture(existing);
        var form = await EditForm(fixture, existing.N);
        form.FabricSurcharges.RemoveAll(s => s.FabricN == 6);
        form.Variants.RemoveAll(v => v.N == 102);
        form.Name = "";
        var returned = Assert.IsType<AdminProductFormViewModel>(Assert.IsType<ViewResult>(await fixture.Controller.ProductEdit(form)).Model);
        Assert.Empty(fixture.Saved);
        Assert.DoesNotContain(returned.FabricSurcharges, s => s.FabricN == 6);
        var retained = Assert.Single(returned.Variants, v => v.N == 102);
        Assert.False(retained.IsActive);
        Assert.False(retained.IsDefault);
        Assert.Equal(4, retained.StockQuantity);
        Assert.Equal(201, retained.ProductImageN);
        Assert.Equal("V-BLUE", retained.Sku);
        // Mimic correcting the form and adding the cached group in a fresh request.
        fixture.Controller.ModelState.Clear();
        returned.Name = "Corrected";
        returned.FabricSurcharges.Add(new() { FabricN = 6, Surcharge = 80m });
        retained.IsActive = true;
        Redirect(await fixture.Controller.ProductEdit(returned));
        Assert.Equal(2, existing.Variants.Count);
        Assert.True(existing.Variants.Single(v => v.N == 102).IsActive);
        Assert.Equal(4, existing.Variants.Single(v => v.N == 102).StockQuantity);
    }
    private static async Task InvalidCreate(Fixture fixture, AdminProductFormViewModel form, string field)
    {
        Assert.IsType<ViewResult>(await fixture.Controller.ProductCreate(form));
        Error(fixture, field);
        Assert.Empty(fixture.Saved);
    }
    private static void Error(Fixture fixture, string field) => Assert.NotEmpty(fixture.Controller.ModelState[field]!.Errors);
    private static void Redirect(IActionResult result) => Assert.IsType<RedirectToActionResult>(result);
    private static async Task<AdminProductFormViewModel> EditForm(Fixture fixture, int n)
        => Assert.IsType<AdminProductFormViewModel>(Assert.IsType<ViewResult>(await fixture.Controller.ProductEdit(n)).Model);
    private static Product Existing() => new() { N = 7, Name = "Original", Sku = "SAVED-01", Price = 75m, RowVersion = [1, 2, 3, 4, 5, 6, 7, 8] };
    private static Product VariantProduct()
    {
        var product = Existing();
        product.OptionKind = ProductOptionKind.Color;
        product.Variants = [new() { N = 101, ProductN = 7, CatalogColorN = 1, Sku = "V-FIRST", IsDefault = true, StockQuantity = 3 }, new() { N = 102, ProductN = 7, CatalogColorN = 2, Sku = "V-SECOND", StockQuantity = 0, Surcharge = 12.34m }];
        return product;
    }
    private static AdminProductFormViewModel BaseForm(Product? existing = null)
        => new() { N = existing?.N ?? 0, Name = "Product", Sku = existing?.Sku ?? "NEW-01", Price = 123.45m, RowVersion = existing == null ? null : Convert.ToBase64String(existing.RowVersion) };
    private static AdminProductFormViewModel ColorForm()
    {
        var form = BaseForm();
        form.OptionKind = ProductOptionKind.Color;
        form.Variants = [new() { CatalogColorN = 1, IsDefault = true, StockQuantity = 3 }, new() { CatalogColorN = 2, StockQuantity = 0, Surcharge = 12.34m }];
        return form;
    }
    private static AdminProductFormViewModel FabricForm()
    {
        var form = BaseForm();
        form.OptionKind = ProductOptionKind.Fabric;
        form.Variants = [new() { CatalogColorN = 10, IsDefault = true, StockQuantity = 2 }, new() { CatalogColorN = 20, StockQuantity = 0 }, new() { CatalogColorN = 21, StockQuantity = null }];
        form.FabricSurcharges = [new() { FabricN = 5, Surcharge = 0 }, new() { FabricN = 6, Surcharge = 80 }];
        return form;
    }

    private sealed class Fixture : IDisposable
    {
        private readonly IdentityDbContext _identity = new(new DbContextOptions<IdentityDbContext>());
        private readonly string _parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Algara.VariantEditor.Tests"));
        private readonly string _directory;
        private readonly ServiceProvider _services;
        public ProductAdminTestContext Db { get; }
        public Fabric[] Fabrics { get; } = [new() { N = 5, Name = "Base fabric", Code = "BASE" }, new() { N = 6, Name = "Velvet", Code = "VELVET" }];
        public CatalogColor[] Colors { get; }
        public List<Product> Saved { get; } = [];
        public AdminController Controller { get; }
        public Fixture(params Product[] products)
        {
            Colors = [new() { N = 1, Name = "Blue", Code = "BLUE" }, new() { N = 2, Name = "Red", Code = "RED" }, new() { N = 10, FabricN = 5, Fabric = Fabrics[0], Name = "Grey", Code = "01" }, new() { N = 20, FabricN = 6, Fabric = Fabrics[1], Name = "Blue", Code = "01" }, new() { N = 21, FabricN = 6, Fabric = Fabrics[1], Name = "Red", Code = "02" }];
            Db = new ProductAdminTestContext(products, [], [], Fabrics, Colors);
            var repository = new Mock<IProductRepository>(MockBehavior.Strict);
            repository.Setup(r => r.AddAsync(It.IsAny<Product>())).Returns((Product p) => { Saved.Add(p); return Task.CompletedTask; });
            repository.Setup(r => r.UpdateAsync(It.IsAny<Product>())).Returns((Product p) => { Saved.Add(p); return Task.CompletedTask; });
            _directory = Path.Combine(_parent, Guid.NewGuid().ToString("N"));
            var environment = new Mock<IWebHostEnvironment>();
            environment.SetupGet(e => e.ContentRootPath).Returns(_directory);
            environment.SetupGet(e => e.WebRootPath).Returns(Path.Combine(_directory, "wwwroot"));
            _services = new ServiceCollection().AddSingleton<IProductGalleryWriteLock, TestLock>().BuildServiceProvider();
            var http = new DefaultHttpContext { RequestServices = _services, User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "admin")], "Test")) };
            Controller = new AdminController(Db, _identity, repository.Object, Mock.Of<ICategoryRepository>(MockBehavior.Strict), Mock.Of<IOrderRepository>(MockBehavior.Strict), Mock.Of<IHeroSlideRepository>(MockBehavior.Strict), Mock.Of<IPromotionRepository>(MockBehavior.Strict), Mock.Of<IUserService>(MockBehavior.Strict), NullLogger<AdminController>.Instance, environment.Object)
            {
                ControllerContext = new ControllerContext { HttpContext = http, RouteData = new RouteData() },
                TempData = new TempDataDictionary(http, Mock.Of<ITempDataProvider>())
            };
            var urls = new Mock<IUrlHelper>();
            urls.Setup(u => u.Action(It.IsAny<UrlActionContext>())).Returns("/admin/products/images/draft");
            Controller.Url = urls.Object;
        }
        public void Dispose()
        {
            _services.Dispose();
            Db.Dispose();
            _identity.Dispose();
            var fullPath = Path.GetFullPath(_directory);
            if (fullPath.StartsWith(_parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && Directory.Exists(fullPath))
                Directory.Delete(fullPath, true);
        }
    }
    private sealed class TestLock : IProductGalleryWriteLock
    {
        public Task<IProductGalleryWriteLease> AcquireAsync(int productN, CancellationToken cancellationToken) => Task.FromResult<IProductGalleryWriteLease>(new Lease());
        private sealed class Lease : IProductGalleryWriteLease
        {
            public Task CommitAsync(CancellationToken cancellationToken) => Task.CompletedTask;
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
