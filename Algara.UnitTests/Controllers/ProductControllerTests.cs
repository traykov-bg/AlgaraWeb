using System.Text.Json;
using Algara.Data.Models;
using Algara.Data.Repositories;
using Algara.Web.Controllers;
using Algara.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Algara.UnitTests.Controllers;

public class ProductControllerTests
{
    private readonly Mock<IProductRepository> _products = new(MockBehavior.Strict);
    private readonly Mock<ICategoryRepository> _categories = new(MockBehavior.Strict);
    private readonly Mock<IUrlHelper> _url = new(MockBehavior.Strict);
    private readonly ProductController _controller;

    public ProductControllerTests()
    {
        _categories.Setup(repository => repository.GetAllAsync())
            .ReturnsAsync(Array.Empty<Category>());
        _controller = new ProductController(
            _products.Object, _categories.Object, NullLogger<ProductController>.Instance);
        _url.Setup(helper => helper.Action(It.IsAny<UrlActionContext>()))
            .Returns((UrlActionContext context) =>
                $"/store/Product/Detail?n={new RouteValueDictionary(context.Values)["n"]}");
        _controller.Url = _url.Object;
    }

    [Theory]
    [InlineData("ДИВАН", 1)]
    [InlineData("ОФИС", 2)]
    [InlineData("дъб", 3)]
    public async Task Index_SearchesNameCategoryAndDescriptionIgnoringCase(string query, int expectedId)
    {
        SetProducts(
            new Product { N = 1, Name = "Ъглов диван" },
            new Product { N = 2, Name = "Стол", Category = new Category { Name = "Офис мебели" } },
            new Product { N = 3, Name = "Маса", Description = "Масивен ДЪБ" },
            new Product { N = 4, Name = "Табуретка" });

        var model = Model(await _controller.Index(q: query));

        Assert.Equal(expectedId, Assert.Single(model.Products).N);
        Assert.Equal(1, model.TotalCount);
        Assert.Equal(query, model.SearchQuery);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Index_EmptyQueryKeepsTheWholeCatalog(string? query)
    {
        SetProducts(Product(1), Product(2));

        var model = Model(await _controller.Index(q: query));

        Assert.Equal(2, model.TotalCount);
        Assert.Equal(2, model.Products.Count);
    }

    [Fact]
    public async Task Index_PriceFilterUsesCurrentPromotionAndIncludesBothBounds()
    {
        var promoted = Product(1, 1000m);
        promoted.ProductPromotions.Add(new ProductPromotion
        {
            OriginalPrice = 1000m,
            PromoPrice = 200m,
            DiscountPercent = 80m,
            Promotion = new Promotion
            {
                IsActive = true, StartDate = DateTime.MinValue, EndDate = DateTime.MaxValue
            }
        });
        var inactivePromotion = Product(5, 900m);
        inactivePromotion.ProductPromotions.Add(new ProductPromotion
        {
            PromoPrice = 250m,
            Promotion = new Promotion
            {
                IsActive = false, StartDate = DateTime.MinValue, EndDate = DateTime.MaxValue
            }
        });
        SetProducts(promoted, Product(2, 300m), Product(3, 199.99m), Product(4, 300.01m), inactivePromotion);

        var model = Model(await _controller.Index(minPrice: 200m, maxPrice: 300m, sort: "name_asc"));

        Assert.Equal(new[] { 1, 2 }, model.Products.Select(product => product.N));
        Assert.Equal(2, model.TotalCount);
        Assert.Equal(200m, model.MinPrice);
        Assert.Equal(300m, model.MaxPrice);
    }

    [Fact]
    public async Task Index_PriceSliderRangeUsesSearchResultsBeforePriceFiltering()
    {
        var lower = Product(1, 100.25m);
        lower.Name = "Диван A";
        var upper = Product(2, 299.75m);
        upper.Name = "Диван B";
        var unrelated = Product(3, 5000m);
        unrelated.Name = "Маса";
        SetProducts(lower, upper, unrelated);

        var model = Model(await _controller.Index(q: "диван", minPrice: 200m));

        Assert.Same(upper, Assert.Single(model.Products));
        Assert.Equal(100m, model.RangeMin);
        Assert.Equal(300m, model.RangeMax);
    }

    [Theory]
    [InlineData(1, 20, 1, 20)]
    [InlineData(2, 20, 21, 20)]
    [InlineData(3, 20, 41, 5)]
    [InlineData(2, 40, 41, 5)]
    public async Task Index_PaginatesWithoutLosingTotalCount(int page, int pageSize, int firstId, int expectedCount)
    {
        SetProducts(Enumerable.Range(1, 45).Select(id => Product(id)).ToArray());

        var model = Model(await _controller.Index(page: page, pageSize: pageSize, sort: "name_asc"));

        Assert.Equal(Enumerable.Range(firstId, expectedCount), model.Products.Select(product => product.N));
        Assert.Equal(45, model.TotalCount);
        Assert.Equal(page, model.Page);
        Assert.Equal(pageSize, model.PageSize);
        Assert.Equal(pageSize == 20 ? 3 : 2, model.TotalPages);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-5, -1)]
    [InlineData(1, 10)]
    [InlineData(1, int.MaxValue)]
    public async Task Index_NormalizesInvalidPageAndPageSize(int page, int pageSize)
    {
        SetProducts(Enumerable.Range(1, 25).Select(id => Product(id)).ToArray());

        var model = Model(await _controller.Index(page: page, pageSize: pageSize, sort: "name_asc"));

        Assert.Equal(1, model.Page);
        Assert.Equal(20, model.PageSize);
        Assert.Equal(Enumerable.Range(1, 20), model.Products.Select(product => product.N));
    }

    [Fact]
    public async Task Index_NoSearchResultsHasAnEmptyPriceRangeAndOnePage()
    {
        SetProducts(Product(1));

        var model = Model(await _controller.Index(q: "missing"));

        Assert.Empty(model.Products);
        Assert.Equal(0, model.TotalCount);
        Assert.Equal(1, model.TotalPages);
        Assert.Equal(0m, model.RangeMin);
        Assert.Equal(0m, model.RangeMax);
    }

    [Theory]
    [InlineData(null, 2, 1, 3)]
    [InlineData("newest", 2, 1, 3)]
    [InlineData("unknown", 2, 1, 3)]
    [InlineData("name_asc", 1, 2, 3)]
    [InlineData("name_desc", 3, 2, 1)]
    [InlineData("price_asc", 2, 3, 1)]
    [InlineData("price_desc", 1, 3, 2)]
    public async Task Index_SortsProductsWithoutPromotions(string? sort, int first, int second, int third)
    {
        var middleByDate = Product(1, 300m);
        var newest = Product(2, 100m);
        var oldest = Product(3, 200m);
        oldest.CreatedAt = new DateTime(2026, 1, 1);
        SetProducts(oldest, middleByDate, newest);

        var model = Model(await _controller.Index(sort: sort));

        Assert.Equal(new[] { first, second, third }, model.Products.Select(product => product.N));
    }

    [Theory]
    [InlineData("price_asc", false)]
    [InlineData("price_desc", true)]
    public async Task Index_SortsByCurrentEffectivePriceIncludingCents(string sort, bool descending)
    {
        var products = ProductsWithDifferentPromotionStates();
        SetProducts(products);

        var model = Model(await _controller.Index(sort: sort));

        var ascendingIds = new[] { 1, 6, 2, 3, 4, 5 };
        Assert.Equal(descending ? ascendingIds.Reverse() : ascendingIds,
            model.Products.Select(product => product.N));
    }

    [Theory]
    [InlineData("price_asc", false)]
    [InlineData("price_desc", true)]
    public async Task Index_PriceFilterAndSortUseTheSameEffectivePrices(string sort, bool descending)
    {
        SetProducts(ProductsWithDifferentPromotionStates());

        var model = Model(await _controller.Index(sort: sort, minPrice: 100.25m, maxPrice: 100.50m));

        var ascendingIds = new[] { 1, 6, 2 };
        Assert.Equal(descending ? ascendingIds.Reverse() : ascendingIds,
            model.Products.Select(product => product.N));
        Assert.Equal(3, model.TotalCount);
        Assert.Equal(100m, model.RangeMin);
        Assert.Equal(351m, model.RangeMax);
    }

    [Fact]
    public async Task Category_UnknownSlugReturnsNotFoundWithoutLoadingProducts()
    {
        _categories.Setup(repository => repository.GetBySlugWithSubCategoriesAsync("missing"))
            .ReturnsAsync((Category?)null);

        Assert.IsType<NotFoundResult>(await _controller.Category("missing"));

        Assert.Empty(_products.Invocations);
    }

    [Fact]
    public async Task Category_ActiveSubcategoryUsesItsProductsAndPreservesSlugNavigation()
    {
        var category = CategoryWithSubcategories();
        var selectedProduct = Product(1);
        SetCategory(category);
        _products.Setup(repository => repository.GetBySubCategoryAsync(11))
            .ReturnsAsync(new[] { selectedProduct });

        var result = Assert.IsType<ViewResult>(await _controller.Category("meka-mebel", sub: "divani"));
        var model = Assert.IsType<CatalogViewModel>(result.Model);

        Assert.Equal("Index", result.ViewName);
        Assert.Same(selectedProduct, Assert.Single(model.Products));
        Assert.Equal("meka-mebel", model.CategorySlug);
        Assert.Equal("Мека мебел", model.CategoryName);
        Assert.Equal("divani", model.ActiveSubSlug);
        Assert.Equal(new[] { "divani", "taburetki" }, model.SubCategories!.Select(sub => sub.Slug));
        _products.Verify(repository => repository.GetBySubCategoryAsync(11), Times.Once);
        _products.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("inactive")]
    [InlineData("from-another-category")]
    public async Task Category_AbsentOrUnavailableSubcategoryUsesParentCategory(string? sub)
    {
        SetCategory(CategoryWithSubcategories());
        var product = Product(1);
        _products.Setup(repository => repository.GetByCategoryAsync(10)).ReturnsAsync(new[] { product });

        var model = Model(await _controller.Category("meka-mebel", sub: sub));

        Assert.Same(product, Assert.Single(model.Products));
        Assert.Null(model.ActiveSubSlug);
        _products.Verify(repository => repository.GetByCategoryAsync(10), Times.Once);
        _products.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("price_asc", false)]
    [InlineData("price_desc", true)]
    public async Task Category_PriceFilterAndSortUseCurrentEffectivePrices(string sort, bool descending)
    {
        SetCategory(CategoryWithSubcategories());
        _products.Setup(repository => repository.GetByCategoryAsync(10))
            .ReturnsAsync(ProductsWithDifferentPromotionStates());

        var model = Model(await _controller.Category("meka-mebel", sort: sort,
            minPrice: 100.25m, maxPrice: 100.50m));

        var ascendingIds = new[] { 1, 6, 2 };
        Assert.Equal(descending ? ascendingIds.Reverse() : ascendingIds,
            model.Products.Select(product => product.N));
        Assert.Equal(3, model.TotalCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("д")]
    public async Task Search_ShortOrEmptyQueryDoesNotLoadCatalog(string? query)
    {
        var result = Assert.IsType<JsonResult>(await _controller.Search(query));

        Assert.Empty(Assert.IsType<object[]>(result.Value));
        Assert.Empty(_products.Invocations);
    }

    [Fact]
    public async Task Search_ReturnsAtMostFiveMatchingSuggestions()
    {
        SetProducts(new[] { new Product { N = 99, Name = "Маса" } }
            .Concat(Enumerable.Range(1, 7).Select(id => new Product { N = id, Name = $"Диван {id}" }))
            .ToArray());

        var result = Assert.IsType<JsonResult>(await _controller.Search("ДИВАН"));
        var suggestions = JsonSerializer.SerializeToElement(result.Value).EnumerateArray().ToArray();

        Assert.Equal(Enumerable.Range(1, 5), suggestions.Select(item => item.GetProperty("n").GetInt32()));
        Assert.All(suggestions, item => Assert.StartsWith("Диван", item.GetProperty("name").GetString()));
    }

    [Theory]
    [InlineData(1, 10025)]
    [InlineData(2, 10050)]
    [InlineData(3, 25075)]
    [InlineData(4, 30099)]
    [InlineData(5, 35090)]
    [InlineData(6, 10045)]
    public async Task Search_ReturnsExactBaseAndCurrentPromotionPrices(int productId, int expectedCents)
    {
        SetProducts(ProductsWithDifferentPromotionStates().Single(product => product.N == productId));

        var result = Assert.IsType<JsonResult>(await _controller.Search("Product"));
        var suggestion = Assert.Single(JsonSerializer.SerializeToElement(result.Value).EnumerateArray());

        Assert.Equal(expectedCents / 100m, suggestion.GetProperty("price").GetDecimal());
    }

    [Fact]
    public async Task Search_ReturnsGeneratedDetailUrlUsingTheProductN()
    {
        SetProducts(Product(42));
        _url.Setup(helper => helper.Action(It.Is<UrlActionContext>(context =>
                context.Action == "Detail" &&
                (int)new RouteValueDictionary(context.Values)["n"]! == 42)))
            .Returns("/shop/offer/42");

        var result = Assert.IsType<JsonResult>(await _controller.Search("Product"));
        var suggestion = Assert.Single(JsonSerializer.SerializeToElement(result.Value).EnumerateArray());

        Assert.Equal("/shop/offer/42", suggestion.GetProperty("url").GetString());
        _url.Verify(helper => helper.Action(It.Is<UrlActionContext>(context =>
            context.Action == "Detail" &&
            (int)new RouteValueDictionary(context.Values)["n"]! == 42)), Times.Once);
    }

    [Fact]
    public async Task Detail_MissingProductReturnsNotFound()
    {
        _products.Setup(repository => repository.GetByNAsync(404)).ReturnsAsync((Product?)null);

        Assert.IsType<NotFoundResult>(await _controller.Detail(404));
    }

    [Fact]
    public async Task Detail_InactiveProductReturnsNotFound()
    {
        var product = Product(42);
        product.IsActive = false;
        _products.Setup(repository => repository.GetByNAsync(42)).ReturnsAsync(product);

        Assert.IsType<NotFoundResult>(await _controller.Detail(42));
    }

    [Fact]
    public async Task Detail_ActiveProductReturnsItsView()
    {
        var product = Product(42);
        _products.Setup(repository => repository.GetByNAsync(42)).ReturnsAsync(product);

        var result = Assert.IsType<ViewResult>(await _controller.Detail(42));

        Assert.Same(product, result.Model);
    }

    private void SetProducts(params Product[] products) =>
        _products.Setup(repository => repository.GetAllAsync()).ReturnsAsync(products);

    private void SetCategory(Category category) =>
        _categories.Setup(repository => repository.GetBySlugWithSubCategoriesAsync(category.Slug))
            .ReturnsAsync(category);

    private static CatalogViewModel Model(IActionResult result) =>
        Assert.IsType<CatalogViewModel>(Assert.IsType<ViewResult>(result).Model);

    private static Product Product(int id, decimal price = 100m) => new()
    {
        N = id,
        Name = $"Product {id:D3}",
        Price = price,
        CreatedAt = new DateTime(2026, 1, 1).AddDays(id)
    };

    private static Product[] ProductsWithDifferentPromotionStates()
    {
        var now = DateTime.Now;
        var active = Product(1, 900m);
        active.ProductPromotions.Add(PromotionPrice(100.25m, true, now.AddYears(-1), now.AddYears(1)));
        var regular = Product(2, 100.50m);
        var inactive = Product(3, 250.75m);
        inactive.ProductPromotions.Add(PromotionPrice(0.01m, false, now.AddYears(-1), now.AddYears(1)));
        var expired = Product(4, 300.99m);
        expired.ProductPromotions.Add(PromotionPrice(0.02m, true, now.AddYears(-2), now.AddYears(-1)));
        var future = Product(5, 350.90m);
        future.ProductPromotions.Add(PromotionPrice(0.03m, true, now.AddYears(1), now.AddYears(2)));
        var multiple = Product(6, 500m);
        multiple.ProductPromotions.Add(PromotionPrice(180.60m, true, now.AddYears(-1), now.AddYears(1)));
        multiple.ProductPromotions.Add(PromotionPrice(100.45m, true, now.AddYears(-1), now.AddYears(1)));
        multiple.ProductPromotions.Add(PromotionPrice(0.04m, false, now.AddYears(-1), now.AddYears(1)));
        return new[] { future, regular, inactive, multiple, active, expired };
    }

    private static ProductPromotion PromotionPrice(decimal price, bool isActive, DateTime start, DateTime end) => new()
    {
        PromoPrice = price,
        Promotion = new Promotion { IsActive = isActive, StartDate = start, EndDate = end }
    };

    private static Category CategoryWithSubcategories() => new()
    {
        N = 10,
        Name = "Мека мебел",
        Slug = "meka-mebel",
        SubCategories = new List<SubCategory>
        {
            new() { N = 12, Name = "Табуретки", Slug = "taburetki", IsActive = true },
            new() { N = 13, Name = "Скрити", Slug = "inactive", IsActive = false },
            new() { N = 11, Name = "Дивани", Slug = "divani", IsActive = true }
        }
    };
}
