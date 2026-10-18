using System.Reflection;
using Algara.Data.Models;
using Algara.Data.Repositories;
using Algara.Identity.Data;
using Algara.Web.Controllers;
using Algara.Web.ViewModels.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SkiaSharp;

namespace Algara.UnitTests.Controllers;

// These tests use supplied rows and a save callback, never SQL Server.
public sealed class AdminOptionCatalogTests
{
    [Fact]
    public async Task FabricCodeIsNormalizedAndDuplicateRejected()
    {
        using var fixture = new Fixture([new Fabric { N = 1, Code = "VELVET", Name = "Velvet" }]);
        var form = new FabricFormViewModel { Code = "  velvet ", Name = "Another" };
        Assert.IsType<ViewResult>(await fixture.Controller.FabricCreate(form));
        Assert.Equal("velvet", form.Code);
        Assert.NotEmpty(fixture.Controller.ModelState[nameof(form.Code)]!.Errors);
        Assert.Equal(0, fixture.Saves);
    }

    [Fact]
    public async Task NewFabricIgnoresPostedIdentityAndSavesNormalizedData()
    {
        using var fixture = new Fixture();
        var form = new FabricFormViewModel { N = 123, Code = " velvet-01 ", Name = " Velvet ", EnglishName = " " };
        Assert.IsType<RedirectToActionResult>(await fixture.Controller.FabricCreate(form));
        var fabric = Assert.Single(fixture.Db.ChangeTracker.Entries<Fabric>()).Entity;
        Assert.Null(form.N);
        Assert.Equal("velvet-01", fabric.Code);
        Assert.Equal("Velvet", fabric.Name);
        Assert.Null(fabric.EnglishName);
        Assert.Equal(1, fixture.Saves);
    }

    [Fact]
    public async Task ColorCannotBeMovedToAnotherFabric()
    {
        var color = new CatalogColor { N = 7, FabricN = 1, Code = "01", Name = "Blue" };
        using var fixture = new Fixture(colors: [color]);
        Assert.IsType<BadRequestResult>(await fixture.Controller.ColorEdit(7, new ColorFormViewModel
        {
            N = 7, FabricN = 2, Code = "01", Name = "Blue", HexColor = "#0000FF"
        }));
        Assert.Equal(1, color.FabricN);
        Assert.Equal(0, fixture.Saves);
    }

    [Fact]
    public async Task BlankFabricCodeUsesNameWithoutWhitespaceAndPreservesCyrillic()
    {
        using var fixture = new Fixture();
        var form = new FabricFormViewModel { Name = "  Синьо кадифе  " };
        Assert.IsType<RedirectToActionResult>(await fixture.Controller.FabricCreate(form));
        var fabric = Assert.Single(fixture.Db.ChangeTracker.Entries<Fabric>()).Entity;
        Assert.Equal("Синьокадифе", fabric.Code);
        Assert.Equal("Синьо кадифе", fabric.Name);
    }

    [Fact]
    public async Task GeneratedFabricCodeStillRequiresUniqueness()
    {
        using var fixture = new Fixture([new Fabric { N = 1, Code = "СИНЬОКАДИФЕ", Name = "Existing" }]);
        var form = new FabricFormViewModel { Name = "Синьо кадифе" };
        Assert.IsType<ViewResult>(await fixture.Controller.FabricCreate(form));
        Assert.NotEmpty(fixture.Controller.ModelState[nameof(form.Code)]!.Errors);
        Assert.Equal(0, fixture.Saves);
    }

    [Fact]
    public async Task LongFabricNameGeneratesCompleteCodeWithoutTruncating()
    {
        using var fixture = new Fixture();
        var form = new FabricFormViewModel { Name = new string('Д', 200) };
        Assert.IsType<RedirectToActionResult>(await fixture.Controller.FabricCreate(form));
        var fabric = Assert.Single(fixture.Db.ChangeTracker.Entries<Fabric>()).Entity;
        Assert.Equal(form.Name, fabric.Code);
        Assert.Equal(200, fabric.Code.Length);
    }

    [Fact]
    public async Task ColorAcceptsCyrillicCodeWithOrdinaryPunctuation()
    {
        using var fixture = new Fixture();
        var form = ColorForm();
        form.Code = "  Синьо/№ 12.а  ";
        Assert.IsType<RedirectToActionResult>(await fixture.Controller.ColorCreate(form));
        var color = Assert.Single(fixture.Db.ChangeTracker.Entries<CatalogColor>()).Entity;
        Assert.Equal("Синьо/№ 12.а", color.Code);
    }

    [Theory]
    [InlineData("Син\u0000ьо")]
    [InlineData("Син\u001Fьо")]
    [InlineData("Син\u007Fьо")]
    [InlineData("Син\u200Bьо")]
    [InlineData("Син\u202Eьо")]
    [InlineData("Син\u034Fьо")]
    [InlineData("Син\uFE0Fьо")]
    public async Task CatalogCodesRejectControlsAndInvisibleCharacters(string code)
    {
        using var fixture = new Fixture();
        var form = ColorForm();
        form.Code = code;
        Assert.IsType<ViewResult>(await fixture.Controller.ColorCreate(form));
        Assert.NotEmpty(fixture.Controller.ModelState[nameof(form.Code)]!.Errors);
        Assert.Equal(0, fixture.Saves);
    }

    [Fact]
    public async Task ColorEditPreservesRetiredGroupData()
    {
        var color = new CatalogColor { N = 7, Code = "BLUE", Name = "Blue", ColorGroup = "Legacy group" };
        using var fixture = new Fixture(colors: [color]);
        var form = ColorForm();
        form.N = 7;
        Assert.IsType<RedirectToActionResult>(await fixture.Controller.ColorEdit(7, form));
        Assert.Equal("Legacy group", color.ColorGroup);
    }

    [Fact]
    public async Task NewFabricIgnoresPostedImageAndAcceptsValidatedUpload()
    {
        using var fixture = new Fixture();
        var form = new FabricFormViewModel { Name = "Дамаска", ImageUrl = "/untrusted.png", ImageFile = Upload() };
        Assert.IsType<RedirectToActionResult>(await fixture.Controller.FabricCreate(form));
        var fabric = Assert.Single(fixture.Db.ChangeTracker.Entries<Fabric>()).Entity;
        Assert.Null(form.ImageUrl);
        Assert.StartsWith("/uploads/products/", fabric.ImageUrl);
        Assert.Single(fixture.Files());
    }

    [Fact]
    public async Task FabricEditPreservesSavedImageAndKeepsOldFileOnReplacement()
    {
        var fabric = new Fabric { N = 1, Code = "Velvet", Name = "Velvet", ImageUrl = "/uploads/products/old.png" };
        using var fixture = new Fixture([fabric]);
        var original = fixture.CreateExistingFile();
        var form = new FabricFormViewModel { N = 1, Name = "Velvet", ImageUrl = "/untrusted.png", ImageFile = Upload() };
        Assert.IsType<RedirectToActionResult>(await fixture.Controller.FabricEdit(1, form));
        Assert.Equal("/uploads/products/old.png", form.ImageUrl);
        Assert.StartsWith("/uploads/products/", fabric.ImageUrl);
        Assert.NotEqual(form.ImageUrl, fabric.ImageUrl);
        Assert.Contains(original, fixture.Files());
        Assert.Equal(2, fixture.Files().Length);
    }

    [Fact]
    public async Task FailedFabricSaveDeletesOnlyNewUpload()
    {
        var fabric = new Fabric { N = 1, Code = "Velvet", Name = "Velvet", ImageUrl = "/uploads/products/old.png" };
        using var fixture = new Fixture([fabric]);
        var original = fixture.CreateExistingFile();
        fixture.FailSave = true;
        var form = new FabricFormViewModel { N = 1, Name = "Velvet", ImageFile = Upload() };
        Assert.IsType<ViewResult>(await fixture.Controller.FabricEdit(1, form));
        Assert.Equal("/uploads/products/old.png", form.ImageUrl);
        Assert.Equal([original], fixture.Files());
    }

    [Fact]
    public async Task InvalidFabricImageNeverSavesOrWritesFiles()
    {
        using var fixture = new Fixture();
        var form = new FabricFormViewModel { Name = "Velvet", ImageFile = new FormFile(new MemoryStream([1, 2, 3]), 0, 3, "ImageFile", "fake.png") };
        Assert.IsType<ViewResult>(await fixture.Controller.FabricCreate(form));
        Assert.NotEmpty(fixture.Controller.ModelState[nameof(form.ImageFile)]!.Errors);
        Assert.Empty(fixture.Files());
        Assert.Equal(0, fixture.Saves);
    }

    [Fact]
    public async Task NewColorRejectsNonexistentFabric()
    {
        using var fixture = new Fixture();
        var form = ColorForm();
        form.FabricN = 999;
        Assert.IsType<ViewResult>(await fixture.Controller.ColorCreate(form));
        Assert.NotEmpty(fixture.Controller.ModelState[nameof(form.FabricN)]!.Errors);
        Assert.Equal(0, fixture.Saves);
    }

    [Fact]
    public async Task ColorCodeMayRepeatInDifferentFabricButNotSameFabric()
    {
        var fabrics = new[] { new Fabric { N = 1, Name = "First" }, new Fabric { N = 2, Name = "Second" } };
        var existing = new CatalogColor { N = 7, FabricN = 1, Code = "BLUE", Name = "Blue" };
        using (var fixture = new Fixture(fabrics, [existing]))
        {
            var form = ColorForm();
            form.FabricN = 1;
            form.Code = " blue ";
            Assert.IsType<ViewResult>(await fixture.Controller.ColorCreate(form));
            Assert.NotEmpty(fixture.Controller.ModelState[nameof(form.Code)]!.Errors);
            Assert.Equal(0, fixture.Saves);
        }
        using (var fixture = new Fixture(fabrics, [existing]))
        {
            var form = ColorForm();
            form.FabricN = 2;
            form.Code = " blue ";
            Assert.IsType<RedirectToActionResult>(await fixture.Controller.ColorCreate(form));
            Assert.Equal(1, fixture.Saves);
        }
    }

    [Fact]
    public async Task NewColorCannotUsePostedUrlAsSwatch()
    {
        using var fixture = new Fixture();
        var form = ColorForm();
        form.HexColor = null;
        form.ImageUrl = "/someone-elses-file.png";
        Assert.IsType<ViewResult>(await fixture.Controller.ColorCreate(form));
        Assert.Null(form.ImageUrl);
        Assert.NotEmpty(fixture.Controller.ModelState[nameof(form.SwatchFile)]!.Errors);
        Assert.Equal(0, fixture.Saves);
    }

    [Fact]
    public async Task EditPreservesTrustedSwatchInsteadOfPostedUrl()
    {
        var color = new CatalogColor { N = 7, Code = "BLUE", Name = "Blue", ImageUrl = "/uploads/products/trusted.png" };
        using var fixture = new Fixture(colors: [color]);
        var form = ColorForm();
        form.N = 7;
        form.ImageUrl = "https://example.invalid/untrusted.png";
        form.HexColor = null;
        Assert.IsType<RedirectToActionResult>(await fixture.Controller.ColorEdit(7, form));
        Assert.Equal("/uploads/products/trusted.png", color.ImageUrl);
        Assert.Equal(1, fixture.Saves);
    }

    [Fact]
    public async Task InvalidSwatchNeverSavesOrWritesFiles()
    {
        using var fixture = new Fixture();
        var form = ColorForm();
        form.SwatchFile = new FormFile(new MemoryStream([1, 2, 3]), 0, 3, "SwatchFile", "fake.png");
        Assert.IsType<ViewResult>(await fixture.Controller.ColorCreate(form));
        Assert.NotEmpty(fixture.Controller.ModelState[nameof(form.SwatchFile)]!.Errors);
        Assert.Empty(fixture.Files());
        Assert.Equal(0, fixture.Saves);
    }

    [Fact]
    public async Task FailedSaveRollsBackOnlyNewlyUploadedSwatch()
    {
        var color = new CatalogColor { N = 7, Code = "BLUE", Name = "Blue", ImageUrl = "/uploads/products/old.png" };
        using var fixture = new Fixture(colors: [color]);
        var original = fixture.CreateExistingFile();
        fixture.FailSave = true;
        var form = ColorForm();
        form.N = 7;
        form.SwatchFile = Upload();
        Assert.IsType<ViewResult>(await fixture.Controller.ColorEdit(7, form));
        Assert.Equal([original], fixture.Files());
        Assert.Equal("/uploads/products/old.png", form.ImageUrl);
        Assert.Equal(1, fixture.Saves);
    }

    [Fact]
    public async Task ReplacementKeepsPreviouslyCommittedSwatchFile()
    {
        var color = new CatalogColor { N = 7, Code = "BLUE", Name = "Blue", ImageUrl = "/uploads/products/old.png" };
        using var fixture = new Fixture(colors: [color]);
        var original = fixture.CreateExistingFile();
        var form = ColorForm();
        form.N = 7;
        form.SwatchFile = Upload();
        Assert.IsType<RedirectToActionResult>(await fixture.Controller.ColorEdit(7, form));
        Assert.StartsWith("/uploads/products/", color.ImageUrl);
        Assert.NotEqual("/uploads/products/old.png", color.ImageUrl);
        Assert.Contains(original, fixture.Files());
        Assert.Equal(2, fixture.Files().Length);
    }

    [Fact]
    public void MvcValidatesRequiredNamesAndColorFormatAndNeverBindsSavedImageUrl()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllersWithViews();
        using var provider = services.BuildServiceProvider();
        var state = new ModelStateDictionary();
        var context = new ActionContext(new DefaultHttpContext { RequestServices = provider }, new RouteData(), new ActionDescriptor(), state);
        provider.GetRequiredService<IObjectModelValidator>().Validate(context, null, string.Empty,
            new ColorFormViewModel { Code = "BLUE", Name = " ", HexColor = "red;display:none" });
        Assert.NotEmpty(state[nameof(ColorFormViewModel.Name)]!.Errors);
        Assert.NotEmpty(state[nameof(ColorFormViewModel.HexColor)]!.Errors);
        var metadata = provider.GetRequiredService<IModelMetadataProvider>().GetMetadataForProperty(
            typeof(ColorFormViewModel), nameof(ColorFormViewModel.ImageUrl));
        Assert.False(metadata.IsBindingAllowed);
        state.Clear();
        provider.GetRequiredService<IObjectModelValidator>().Validate(context, null, string.Empty,
            new FabricFormViewModel { Name = "Дамаска" });
        Assert.True(state.IsValid);
        var fabricImageMetadata = provider.GetRequiredService<IModelMetadataProvider>().GetMetadataForProperty(
            typeof(FabricFormViewModel), nameof(FabricFormViewModel.ImageUrl));
        Assert.False(fabricImageMetadata.IsBindingAllowed);
    }

    [Fact]
    public void CatalogMutationsKeepStaffPermissionAuthorizationAndAntiforgery()
    {
        Assert.Contains(typeof(AdminController).GetCustomAttributes<AuthorizeAttribute>(), a => a.Policy == "Staff");
        var actions = typeof(AdminController).GetMethods().Where(m =>
            m.Name is "FabricCreate" or "FabricEdit" or "ColorCreate" or "ColorEdit");
        foreach (var action in actions)
        {
            Assert.Null(action.GetCustomAttribute<AllowAnonymousAttribute>());
            Assert.NotEmpty(action.GetCustomAttributes<Algara.Web.Authorization.RequirePermissionAttribute>());
            if (action.GetCustomAttribute<HttpPostAttribute>() is not null)
                Assert.NotNull(action.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
        }
    }

    private static ColorFormViewModel ColorForm() => new() { Code = "BLUE", Name = "Blue", HexColor = "#0000FF" };

    private static IFormFile Upload()
    {
        using var bitmap = new SKBitmap(2, 2);
        bitmap.Erase(SKColors.Blue);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 90);
        var bytes = encoded.ToArray();
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "SwatchFile", "blue.png");
    }

    private sealed class Fixture : IDisposable
    {
        private readonly StaffAccessTestContext _identityDb = new();
        private readonly ServiceProvider _services;
        private readonly string _ownedParent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Algara.OptionCatalog.Tests"));
        private readonly string _root;
        public ProductAdminTestContext Db { get; }
        public AdminController Controller { get; }
        public int Saves { get; private set; }
        public bool FailSave { get; set; }

        public Fixture(Fabric[]? fabrics = null, CatalogColor[]? colors = null)
        {
            Db = new ProductAdminTestContext([], [], [], fabrics, colors, () =>
            {
                Saves++;
                if (FailSave) throw new DbUpdateException("Simulated failed save");
                return 1;
            });
            _root = Path.Combine(_ownedParent, Guid.NewGuid().ToString("N"));
            var environment = new Mock<IWebHostEnvironment>();
            environment.SetupGet(e => e.WebRootPath).Returns(Path.Combine(_root, "wwwroot"));
            _services = _identityDb.AddServices(new ServiceCollection()).BuildServiceProvider();
            var http = new DefaultHttpContext { RequestServices = _services, User = StaffAccessTestContext.Principal() };
            Controller = new AdminController(Db, _identityDb, Mock.Of<IProductRepository>(), Mock.Of<ICategoryRepository>(),
                Mock.Of<IOrderRepository>(), Mock.Of<IHeroSlideRepository>(), Mock.Of<IPromotionRepository>(),
                Mock.Of<IUserService>(), NullLogger<AdminController>.Instance, environment.Object)
            {
                ControllerContext = new ControllerContext { HttpContext = http },
                TempData = new TempDataDictionary(http, Mock.Of<ITempDataProvider>())
            };
        }

        public string CreateExistingFile()
        {
            var path = Path.Combine(_root, "wwwroot", "uploads", "products", "old.png");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, [1, 2, 3]);
            return path;
        }

        public string[] Files() => Directory.Exists(_root) ? Directory.GetFiles(_root, "*", SearchOption.AllDirectories) : [];

        public void Dispose()
        {
            _services.Dispose();
            Db.Dispose();
            _identityDb.Dispose();
            var resolved = Path.GetFullPath(_root);
            if (resolved.StartsWith(_ownedParent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && Directory.Exists(resolved))
                Directory.Delete(resolved, true);
        }
    }
}
