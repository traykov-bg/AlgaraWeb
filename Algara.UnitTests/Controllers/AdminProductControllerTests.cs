using System.Security.Claims;
using System.Text.Json;
using Algara.Web.Services;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.DependencyInjection;
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
using SkiaSharp;

namespace Algara.UnitTests.Controllers;

// Direct calls cover controller decisions and graph mutations. MVC binding, attributes,
// authorization, database constraints and rendered forms are separate integration checks.
public sealed class AdminProductControllerTests
{
    [Fact]
    public async Task CreateGet_ReturnsActiveCategoriesAndEmptyGallery()
    {
        using var fixture = new Fixture();
        var model = Model(await fixture.Controller.ProductCreate());
        Assert.Equal([1, 2], model.Categories.Select(c => c.N).Order().ToArray());
        Assert.Empty(model.Images);
        Assert.Empty(fixture.Added);
    }

    [Fact]
    public async Task CreatePost_SavesNormalizedCodeAndCompleteProductGraph()
    {
        using var fixture = new Fixture();
        var form = Form();
        form.Sku = "  sofa-a_01  ";
        form.Name = "  Base product  ";
        form.Description = "  Base description  ";
        form.Materials = "  Wood  ";
        form.EnglishName = "  Sofa  ";
        form.EnglishDescription = "  English description  ";
        form.EnglishMaterials = "  Oak  ";
        form.WidthCm = 210.5m;
        form.HeightCm = 90m;
        form.DepthCm = 95.25m;
        form.IsFeatured = true;
        form.IsCustomizable = true;
        form.SelectedSubCategoryNs = [10, 10];

        Redirect(await fixture.Controller.ProductCreate(form));

        var saved = Assert.Single(fixture.Added);
        Assert.Equal("SOFA-A_01", saved.Sku);
        Assert.Equal("Base product", saved.Name);
        Assert.Equal("Base description", saved.Description);
        Assert.Equal("Wood", saved.Materials);
        Assert.Equal(210.5m, saved.WidthCm);
        Assert.Equal(90m, saved.HeightCm);
        Assert.Equal(95.25m, saved.DepthCm);
        Assert.Equal(123.45m, saved.Price);
        Assert.True(saved.IsCustomizable);
        Assert.True(saved.IsFeatured);
        Assert.True(saved.IsActive);
        Assert.Equal(1, saved.CategoryN);
        Assert.Equal(10, Assert.Single(saved.ProductSubCategories).SubCategoryN);
        var english = Assert.Single(saved.Translations);
        Assert.Equal("en", english.Language);
        Assert.Equal("Sofa", english.Name);
        Assert.Equal("English description", english.Description);
        Assert.Equal("Oak", english.Materials);
        Assert.Empty(saved.Images);
        Assert.Null(saved.ImageUrl);
    }

    [Fact]
    public async Task CreatePost_EmptySkuIsPassedToRepositoryForAutomaticAllocation()
    {
        using var fixture = new Fixture();
        var form = Form();
        form.Sku = "  ";
        Redirect(await fixture.Controller.ProductCreate(form));
        Assert.Equal(string.Empty, fixture.SkuBeforeSave);
        Assert.Single(fixture.Added);
    }

    [Theory]
    [InlineData("CODE WITH SPACE")]
    [InlineData("../../FILE")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public async Task CreatePost_InvalidSkuReturnsValidationWithoutSaving(string sku)
    {
        using var fixture = new Fixture();
        var form = Form();
        form.Sku = sku;
        Assert.Same(form, Model(await fixture.Controller.ProductCreate(form)));
        AssertError(fixture, nameof(form.Sku));
        Assert.Empty(fixture.Added);
    }

    [Fact]
    public async Task CreatePost_DuplicateSkuIsNormalizedBeforeCheckingAndNeverSaves()
    {
        using var fixture = new Fixture(Existing());
        var form = Form();
        form.Sku = "  saved-01  ";
        Model(await fixture.Controller.ProductCreate(form));
        AssertError(fixture, nameof(form.Sku));
        Assert.Empty(fixture.Added);
    }

    [Fact]
    public async Task EditGet_ReturnsTranslationsDimensionsAndSortedTrustedImages()
    {
        var product = Existing();
        product.WidthCm = 12.5m;
        product.Materials = "Wood";
        product.Translations.Add(new ProductTranslation { Language = "en", Name = "English", Description = "Details" });
        product.Images.Add(new ProductImage { N = 102, ImageUrl = "/second.png", DisplayOrder = 5 });
        product.Images.Add(new ProductImage { N = 101, ImageUrl = "/first.png", DisplayOrder = 0 });
        using var fixture = new Fixture(product);

        var form = Model(await fixture.Controller.ProductEdit(product.N));

        Assert.Equal(product.N, form.N);
        Assert.Equal(product.Sku, form.Sku);
        Assert.Equal(12.5m, form.WidthCm);
        Assert.Equal("English", form.EnglishName);
        Assert.Equal("Details", form.EnglishDescription);
        Assert.Equal([101, 102], form.Images.Select(i => i.N).ToArray());
        Assert.Equal("/first.png", form.Images[0].ImageUrl);
    }

    [Fact]
    public async Task EditPost_PreservesIdentityAndSkuWhenCodeIsBlank()
    {
        var product = Existing();
        var created = product.CreatedAt;
        using var fixture = new Fixture(product);
        var form = Form(product.N);
        form.Name = "Changed";
        form.Sku = string.Empty;

        Redirect(await fixture.Controller.ProductEdit(form));

        Assert.Same(product, Assert.Single(fixture.Updated));
        Assert.Equal(7, product.N);
        Assert.Equal("SAVED-01", product.Sku);
        Assert.Equal(created, product.CreatedAt);
        Assert.Equal("Changed", product.Name);
    }

    [Fact]
    public async Task EditPost_AllowsSameNormalizedSkuAndRemovesEmptyEnglishTranslation()
    {
        var product = Existing();
        product.Translations.Add(new ProductTranslation { Language = "en", Name = "English name" });
        using var fixture = new Fixture(product);
        var form = Form(product.N);
        form.Sku = "  saved-01  ";
        form.EnglishName = " ";
        form.EnglishDescription = " ";

        Redirect(await fixture.Controller.ProductEdit(form));

        Assert.Empty(product.Translations);
        Assert.Equal(form.Name, product.GetName("en"));
        Assert.Single(fixture.Updated);
    }

    [Theory]
    [InlineData(999)]
    [InlineData(3)]
    public async Task CreatePost_RejectsMissingOrInactiveCategory(int category)
    {
        using var fixture = new Fixture();
        var form = Form();
        form.CategoryN = category;
        Model(await fixture.Controller.ProductCreate(form));
        AssertError(fixture, nameof(form.CategoryN));
        Assert.Empty(fixture.Added);
    }

    [Theory]
    [InlineData(999)]
    [InlineData(20)]
    [InlineData(11)]
    public async Task CreatePost_RejectsUnknownOtherCategoryOrInactiveSubcategory(int subcategory)
    {
        using var fixture = new Fixture();
        var form = Form();
        form.SelectedSubCategoryNs = [subcategory];
        Model(await fixture.Controller.ProductCreate(form));
        AssertError(fixture, nameof(form.SelectedSubCategoryNs));
        Assert.Empty(fixture.Added);
    }

    [Fact]
    public async Task EditPost_KeepsAlreadyAssignedInactiveCategoryAndSubcategory()
    {
        var product = Existing();
        product.CategoryN = 3;
        product.ProductSubCategories.Add(new ProductSubCategory { SubCategoryN = 30 });
        using var fixture = new Fixture(product);
        var form = Form(product.N);
        form.CategoryN = 3;
        form.SelectedSubCategoryNs = [30];
        Redirect(await fixture.Controller.ProductEdit(form));
        Assert.Equal(30, Assert.Single(product.ProductSubCategories).SubCategoryN);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EditPost_RejectsForeignOrDuplicateImageIdsAndRestoresTrustedUrls(bool duplicate)
    {
        var product = GalleryProduct();
        using var fixture = new Fixture(product);
        var form = Form(product.N);
        form.Images = duplicate
            ? [new() { N = 101, ImageUrl = "https://forged.example/one" }, new() { N = 101 }]
            : [new() { N = 999, ImageUrl = "https://forged.example/two" }];
        Model(await fixture.Controller.ProductEdit(form));
        AssertError(fixture, nameof(form.Images));
        Assert.Empty(fixture.Updated);
        Assert.Equal(["/first.png", "/second.png", "/third.png"], form.Images.Select(i => i.ImageUrl).ToArray());
        Assert.Equal(3, product.Images.Count);
    }

    [Fact]
    public async Task EditPost_RejectsOmittedConcurrentImageAndRestoresCurrentGallery()
    {
        var product = GalleryProduct();
        using var fixture = new Fixture(product);
        var form = Form(product.N);
        form.Images = [new() { N = 101, DisplayOrder = 0 }];
        Model(await fixture.Controller.ProductEdit(form));
        AssertError(fixture, nameof(form.Images));
        Assert.Empty(fixture.Updated);
        Assert.Equal([101, 102, 103], form.Images.Select(i => i.N));
    }

    [Fact]
    public async Task EditPost_OrderSelectsMainImageWithoutTrustingPostedUrls()
    {
        var product = GalleryProduct();
        using var fixture = new Fixture(product);
        var form = Form(product.N);
        form.Images = [new() { N = 101, DisplayOrder = 2 }, new() { N = 102, DisplayOrder = 0, ImageUrl = "/forged.png" },
            new() { N = 103, DisplayOrder = 1 }];
        Redirect(await fixture.Controller.ProductEdit(form));
        var images = product.Images.OrderBy(i => i.DisplayOrder).ToArray();
        Assert.Equal([102, 103, 101], images.Select(i => i.N).ToArray());
        Assert.Equal([0, 1, 2], images.Select(i => i.DisplayOrder).ToArray());
        Assert.Equal("/second.png", product.ImageUrl);
        Assert.Equal("/second.png", product.GetMainImageUrl());
    }

    [Fact]
    public async Task Delete_LastImageImmediatelyClearsLegacyMainUrl()
    {
        var product = Existing();
        product.Images.Add(new ProductImage { N = 101, ImageUrl = "/old.png" });
        product.ImageUrl = "/old.png";
        using var fixture = new Fixture(product);
        Assert.IsType<JsonResult>(await fixture.Controller.ProductImageDelete(7, null, 101, null));
        Assert.Empty(product.Images);
        Assert.Null(product.ImageUrl);
        Assert.Single(fixture.Updated);
    }

    [Fact]
    public async Task Upload_ImmediatelyAppendsWithoutChangingSavedMainOrOtherFields()
    {
        var product = GalleryProduct();
        using var fixture = new Fixture(product);
        Assert.IsType<JsonResult>(await fixture.Controller.ProductImagesUpload(7, null, [Upload()]));
        Assert.Equal(4, product.Images.Count);
        Assert.Equal("/first.png", product.ImageUrl);
        Assert.Equal("Original", product.Name);
        Assert.Equal(75m, product.Price);
        Assert.Single(fixture.UploadedFiles());
        Assert.Single(fixture.Updated);
    }

    [Fact]
    public async Task Upload_InvalidFileDoesNotMutateOrSaveProduct()
    {
        var product = GalleryProduct();
        using var fixture = new Fixture(product);
        var invalid = new FormFile(new MemoryStream([1, 2, 3]), 0, 3, "files", "fake.jpg");
        var result = Assert.IsType<JsonResult>(await fixture.Controller.ProductImagesUpload(7, null, [invalid]));
        Assert.Equal(400, result.StatusCode);
        Assert.Empty(fixture.Updated);
        Assert.Equal(3, product.Images.Count);
        Assert.Empty(fixture.UploadedFiles());
    }

    [Fact]
    public async Task CreatePost_ConcurrentSkuConflictRetainsPrivateDraftAndRollsBackPublishedCopies()
    {
        using var fixture = new Fixture();
        var form = await DraftForm(fixture);
        fixture.Products.Setup(p => p.AddAsync(It.IsAny<Product>())).ThrowsAsync(new ProductSkuConflictException("NEW-01"));
        Model(await fixture.Controller.ProductCreate(form));
        AssertError(fixture, nameof(form.Sku));
        Assert.Empty(fixture.UploadedFiles());
        Assert.Single(form.Images);
        var preview = await fixture.Controller.ProductImageDraft(form.GalleryDraftId!, form.Images[0].UploadId!);
        Assert.IsType<FileContentResult>(preview);
    }

    [Fact]
    public async Task EditPost_FailedSaveRestoresTrustedGalleryAndKeepsProposedOrder()
    {
        var product = GalleryProduct();
        using var fixture = new Fixture(product);
        fixture.Products.Setup(p => p.UpdateAsync(It.IsAny<Product>())).ThrowsAsync(new DbUpdateException("Expected failure"));
        var form = Form(product.N);
        form.Images = [new() { N = 101, DisplayOrder = 2 }, new() { N = 102, DisplayOrder = 0 }, new() { N = 103, DisplayOrder = 1 }];
        Model(await fixture.Controller.ProductEdit(form));
        AssertError(fixture, string.Empty);
        Assert.Equal([102, 103, 101], form.Images.Select(i => i.N).ToArray());
        Assert.Equal("/second.png", form.Images[0].ImageUrl);
        Assert.Empty(fixture.UploadedFiles());
    }

    [Fact]
    public async Task CreatePost_PublishesPrivateDraftOnlyAfterValidationAndConsumesIt()
    {
        using var fixture = new Fixture();
        var form = await DraftForm(fixture);
        Assert.Empty(fixture.Added);
        Assert.Empty(fixture.UploadedFiles());
        Redirect(await fixture.Controller.ProductCreate(form));
        var product = Assert.Single(fixture.Added);
        Assert.StartsWith("/uploads/products/", product.ImageUrl);
        Assert.Single(fixture.UploadedFiles());
        Assert.IsType<NotFoundResult>(await fixture.Controller.ProductImageDraft(form.GalleryDraftId!, form.Images[0].UploadId!));
    }

    [Fact]
    public async Task CreatePost_ValidationFailureRetainsDraftAndProposedOrder()
    {
        using var fixture = new Fixture();
        var form = await DraftForm(fixture);
        form.Name = "";
        Model(await fixture.Controller.ProductCreate(form));
        AssertError(fixture, nameof(form.Name));
        Assert.Single(form.Images);
        Assert.IsType<FileContentResult>(await fixture.Controller.ProductImageDraft(form.GalleryDraftId!, form.Images[0].UploadId!));
        Assert.Empty(fixture.Added);
        Assert.Empty(fixture.UploadedFiles());
    }

    [Fact]
    public async Task Draft_AnotherAuthenticatedOwnerCannotPreviewDeleteOrAttach()
    {
        using var fixture = new Fixture();
        var form = await DraftForm(fixture);
        fixture.Controller.HttpContext.User = Principal("other-admin");
        Assert.IsType<NotFoundResult>(await fixture.Controller.ProductImageDraft(form.GalleryDraftId!, form.Images[0].UploadId!));
        var deletion = Assert.IsType<JsonResult>(await fixture.Controller.ProductImageDelete(0, form.GalleryDraftId, 0, form.Images[0].UploadId));
        Assert.Equal(400, deletion.StatusCode);
        Model(await fixture.Controller.ProductCreate(form));
        AssertError(fixture, nameof(form.Images));
        Assert.Empty(fixture.Added);
    }

    [Fact]
    public async Task Delete_ForeignImageCannotAffectEitherProduct()
    {
        var product = GalleryProduct();
        using var fixture = new Fixture(product);
        var deletion = Assert.IsType<JsonResult>(await fixture.Controller.ProductImageDelete(7, null, 999, null));
        Assert.Equal(404, deletion.StatusCode);
        Assert.Equal(3, product.Images.Count);
        Assert.Empty(fixture.Updated);
    }

    private static async Task<AdminProductFormViewModel> DraftForm(Fixture fixture)
    {
        var draft = Model(await fixture.Controller.ProductCreate()).GalleryDraftId!;
        var upload = Assert.IsType<JsonResult>(await fixture.Controller.ProductImagesUpload(0, draft, [Upload()]));
        var data = JsonSerializer.SerializeToElement(upload.Value);
        var uploadId = data.GetProperty("images")[0].GetProperty("UploadId").GetString();
        var form = Form();
        form.GalleryDraftId = draft;
        form.Images = [new() { UploadId = uploadId, DisplayOrder = 0 }];
        return form;
    }

    [Fact]
    public async Task EditPost_MismatchedRouteAndHiddenIdAreRejected()
    {
        using var fixture = new Fixture(Existing());
        fixture.Controller.RouteData.Values["n"] = "999";
        Assert.IsType<BadRequestResult>(await fixture.Controller.ProductEdit(Form(7)));
        Assert.Empty(fixture.Updated);
    }

    [Fact]
    public async Task Edit_GetAndPostMissingProductsReturnNotFound()
    {
        using var fixture = new Fixture();
        Assert.IsType<NotFoundResult>(await fixture.Controller.ProductEdit(99));
        Assert.IsType<NotFoundResult>(await fixture.Controller.ProductEdit(Form(99)));
    }

    [Fact]
    public async Task Upload_RejectsGalleryAboveTwentyImagesWithoutWritingFiles()
    {
        var product = Existing();
        product.Images = Enumerable.Range(1, 20).Select(n => new ProductImage { N = n, ImageUrl = $"/{n}.png" }).ToList();
        using var fixture = new Fixture(product);
        var result = Assert.IsType<JsonResult>(await fixture.Controller.ProductImagesUpload(7, null, [Upload()]));
        Assert.Equal(400, result.StatusCode);
        Assert.Empty(fixture.Updated);
        Assert.Empty(fixture.UploadedFiles());
    }

    private static ClaimsPrincipal Principal(string id) => StaffAccessTestContext.Principal(id);

    private static Product Existing() => new() { N = 7, Sku = "SAVED-01", Name = "Original", Price = 75m, CategoryN = 1 };
    private static Product GalleryProduct()
    {
        var product = Existing();
        product.ImageUrl = "/first.png";
        product.Images = [new() { N = 101, ImageUrl = "/first.png", DisplayOrder = 0 },
            new() { N = 102, ImageUrl = "/second.png", DisplayOrder = 1 }, new() { N = 103, ImageUrl = "/third.png", DisplayOrder = 2 }];
        return product;
    }
    private static AdminProductFormViewModel Form(int n = 0) => new() { N = n, Name = "Product", Sku = "NEW-01", Price = 123.45m, CategoryN = 1 };
    private static AdminProductFormViewModel Model(IActionResult result) => Assert.IsType<AdminProductFormViewModel>(Assert.IsType<ViewResult>(result).Model);
    private static void Redirect(IActionResult result) => Assert.Equal(nameof(AdminController.Products), Assert.IsType<RedirectToActionResult>(result).ActionName);
    private static void AssertError(Fixture fixture, string field) => Assert.NotEmpty(fixture.Controller.ModelState[field]!.Errors);
    private static IFormFile Upload()
    {
        using var bitmap = new SKBitmap(2, 2);
        bitmap.Erase(SKColors.Blue);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 90);
        var bytes = encoded.ToArray();
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "NewImages", "photo.png");
    }

    private sealed class TestGalleryLock : IProductGalleryWriteLock
    {
        public Task<IProductGalleryWriteLease> AcquireAsync(int productN, CancellationToken cancellationToken)
            => Task.FromResult<IProductGalleryWriteLease>(new TestGalleryLease());
        private sealed class TestGalleryLease : IProductGalleryWriteLease
        {
            public Task CommitAsync(CancellationToken cancellationToken) => Task.CompletedTask;
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly StaffAccessTestContext _identityDb = new();
        private readonly string _testRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Algara.AdminProduct.Tests"));
        private readonly string _root;
        private readonly ServiceProvider _services;
        public ProductAdminTestContext Db { get; }
        public Mock<IProductRepository> Products { get; } = new(MockBehavior.Strict);
        public List<Product> Added { get; } = [];
        public List<Product> Updated { get; } = [];
        public string? SkuBeforeSave { get; private set; }
        public AdminController Controller { get; }

        public Fixture(params Product[] products)
        {
            Db = new ProductAdminTestContext(products,
                [new() { N = 1, Name = "Category A", IsActive = true }, new() { N = 2, Name = "Category B", IsActive = true },
                 new() { N = 3, Name = "Category C", IsActive = false }],
                [new() { N = 10, Name = "Active", CategoryN = 1, IsActive = true }, new() { N = 11, Name = "Inactive", CategoryN = 1, IsActive = false },
                 new() { N = 20, Name = "Other", CategoryN = 2, IsActive = true }, new() { N = 30, Name = "Existing inactive", CategoryN = 3, IsActive = false }]);
            Products.Setup(p => p.AddAsync(It.IsAny<Product>())).Returns((Product product) =>
            {
                SkuBeforeSave = product.Sku;
                product.N = 101;
                if (product.Sku.Length == 0) product.Sku = "000101";
                Added.Add(product);
                return Task.CompletedTask;
            });
            Products.Setup(p => p.UpdateAsync(It.IsAny<Product>())).Returns((Product product) => { Updated.Add(product); return Task.CompletedTask; });
            _root = Path.Combine(_testRoot, Guid.NewGuid().ToString("N"));
            var environment = new Mock<IWebHostEnvironment>();
            environment.SetupGet(e => e.WebRootPath).Returns(Path.Combine(_root, "wwwroot"));
            environment.SetupGet(e => e.ContentRootPath).Returns(_root);
            var services = new ServiceCollection();
            services.AddSingleton<IProductGalleryWriteLock, TestGalleryLock>();
            _identityDb.AddServices(services);
            _services = services.BuildServiceProvider();
            var http = new DefaultHttpContext { RequestServices = _services, User = Principal("admin") };
            Controller = new AdminController(Db, _identityDb, Products.Object,
                Mock.Of<ICategoryRepository>(MockBehavior.Strict), Mock.Of<IOrderRepository>(MockBehavior.Strict),
                Mock.Of<IHeroSlideRepository>(MockBehavior.Strict), Mock.Of<IPromotionRepository>(MockBehavior.Strict),
                Mock.Of<IUserService>(MockBehavior.Strict), NullLogger<AdminController>.Instance, environment.Object)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = http,
                    RouteData = new Microsoft.AspNetCore.Routing.RouteData()
                },
                TempData = new TempDataDictionary(http, Mock.Of<ITempDataProvider>())
            };
            var urls = new Mock<IUrlHelper>();
            urls.Setup(u => u.Action(It.IsAny<UrlActionContext>())).Returns("/admin/products/images/draft");
            Controller.Url = urls.Object;
        }
        public string[] UploadedFiles() => Directory.Exists(Path.Combine(_root, "wwwroot")) ? Directory.GetFiles(Path.Combine(_root, "wwwroot"), "*", SearchOption.AllDirectories) : [];
        public void Dispose()
        {
            _services.Dispose();
            Db.Dispose();
            _identityDb.Dispose();
            var resolvedRoot = Path.GetFullPath(_root);
            if (resolvedRoot.StartsWith(_testRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && Directory.Exists(resolvedRoot))
                Directory.Delete(resolvedRoot, true);
        }
    }
}
