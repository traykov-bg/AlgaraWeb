using Algara.Data.Models;
using Algara.Data.Repositories;
using Algara.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Algara.UnitTests.Controllers;

public class ProductVariantSelectionTests
{
    [Theory]
    [InlineData(null, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 1)]
    [InlineData(4, 1)]
    [InlineData(999, 1)]
    public async Task Detail_SelectsOnlyAnActiveVariantOwnedByThisProduct(int? requested, int expected)
    {
        var product = new Product
        {
            N = 7, Name = "Table", OptionKind = ProductOptionKind.Color,
            Variants =
            [
                new ProductVariant { N = 1, ProductN = 7, IsDefault = true, Color = new CatalogColor { N = 10, Name = "Oak" } },
                new ProductVariant { N = 2, ProductN = 7, Color = new CatalogColor { N = 11, Name = "White" } },
                new ProductVariant { N = 3, ProductN = 7, IsActive = false, Color = new CatalogColor { N = 12, Name = "Black" } },
                new ProductVariant { N = 4, ProductN = 8, Color = new CatalogColor { N = 13, Name = "Other product" } }
            ]
        };
        var products = new Mock<IProductRepository>();
        products.Setup(repository => repository.GetByNAsync(7)).ReturnsAsync(product);
        var controller = new ProductController(products.Object, Mock.Of<ICategoryRepository>(), NullLogger<ProductController>.Instance);

        var result = Assert.IsType<ViewResult>(await controller.Detail(7, requested));

        Assert.Same(product, result.Model);
        Assert.Equal(expected, result.ViewData["SelectedVariantN"]);
    }
}
