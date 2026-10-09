using System.Globalization;
using System.Text.Json;
using Algara.Data.Models;
using Algara.Data.Repositories;
using Algara.Web.Controllers;
using Algara.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Algara.UnitTests.Controllers;

public class ProductTranslationControllerTests
{
    [Theory]
    [InlineData("SOFA", 1)]
    [InlineData("oak", 1)]
    [InlineData("000002", 2)]
    [InlineData("Стол", 2)]
    public async Task EnglishCatalog_SearchesTranslatedFieldsSkuAndBulgarianFallback(string query, int expectedId)
    {
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
            var controller = CreateController();

            var result = Assert.IsType<ViewResult>(await controller.Index(q: query));

            Assert.Equal(expectedId, Assert.Single(Assert.IsType<CatalogViewModel>(result.Model).Products).N);
        }
        finally { CultureInfo.CurrentUICulture = original; }
    }

    [Theory]
    [InlineData("bg", 1, 3)]
    [InlineData("en", 3, 1)]
    public async Task Catalog_NameSortUsesSelectedDisplayNames(string language, int first, int second)
    {
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
            var controller = CreateController();

            var result = Assert.IsType<ViewResult>(await controller.Index(q: "sorting", sort: "name_asc"));

            Assert.Equal(new[] { first, second }, Assert.IsType<CatalogViewModel>(result.Model).Products.Select(p => p.N));
        }
        finally { CultureInfo.CurrentUICulture = original; }
    }

    [Fact]
    public async Task LiveSearch_UsesEnglishNameAndMainGalleryImageWithoutChangingPrice()
    {
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
            var controller = CreateController();

            var result = Assert.IsType<JsonResult>(await controller.Search("000001"));
            var item = Assert.Single(JsonSerializer.SerializeToElement(result.Value).EnumerateArray());

            Assert.Equal("Sofa", item.GetProperty("name").GetString());
            Assert.Equal("/images/main.jpg", item.GetProperty("imageUrl").GetString());
            Assert.Equal(123.45m, item.GetProperty("price").GetDecimal());
        }
        finally { CultureInfo.CurrentUICulture = original; }
    }

    private static ProductController CreateController()
    {
        var products = new Mock<IProductRepository>();
        products.Setup(repository => repository.GetAllAsync()).ReturnsAsync(new[]
        {
            new Product
            {
                N = 1, Sku = "000001", Name = "Диван", Description = "sorting", Price = 123.45m,
                ImageUrl = "/images/legacy.jpg",
                Translations = [new ProductTranslation { Language = "en", Name = "Sofa", Description = "Oak sorting" }],
                Images = [new ProductImage { N = 2, ImageUrl = "/images/second.jpg", DisplayOrder = 1 },
                    new ProductImage { N = 3, ImageUrl = "/images/main.jpg", DisplayOrder = 0 }]
            },
            new Product
            {
                N = 2, Sku = "000002", Name = "Стол",
                Translations = [new ProductTranslation { Language = "en", Name = " " }]
            },
            new Product
            {
                N = 3, Sku = "000003", Name = "Маса", Description = "sorting",
                Translations = [new ProductTranslation { Language = "en", Name = "Desk" }]
            }
        });
        var categories = new Mock<ICategoryRepository>();
        categories.Setup(repository => repository.GetAllAsync()).ReturnsAsync(Array.Empty<Category>());
        var urls = new Mock<IUrlHelper>();
        urls.Setup(helper => helper.Action(It.IsAny<UrlActionContext>())).Returns("/Product/Detail?n=1");
        return new ProductController(products.Object, categories.Object, NullLogger<ProductController>.Instance) { Url = urls.Object };
    }
}
