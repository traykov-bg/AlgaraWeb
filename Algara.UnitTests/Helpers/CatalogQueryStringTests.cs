using Algara.UnitTests.Models;
using Algara.Web.Helpers;
using Algara.Web.ViewModels;
using Microsoft.AspNetCore.WebUtilities;

namespace Algara.UnitTests.Helpers;

public class CatalogQueryStringTests
{
    [Fact]
    public void All_subcategories_clears_selection_and_resets_page_while_preserving_filters()
    {
        var model = FilteredCatalog();

        var query = QueryHelpers.ParseQuery(CatalogQueryString.ForSubcategory(model, null));

        Assert.False(query.ContainsKey("sub"));
        Assert.Equal("1", query["page"].ToString());
        Assert.Equal(model.SearchQuery, query["q"].ToString());
        Assert.Equal(model.Sort, query["sort"].ToString());
        Assert.Equal("40", query["pageSize"].ToString());
        Assert.Equal("100.25", query["minPrice"].ToString());
        Assert.Equal("999.99", query["maxPrice"].ToString());
    }

    [Fact]
    public void Changing_subcategory_replaces_selection_and_resets_page()
    {
        var query = QueryHelpers.ParseQuery(CatalogQueryString.ForSubcategory(FilteredCatalog(), "stolove"));

        Assert.Equal("stolove", query["sub"].ToString());
        Assert.Equal("1", query["page"].ToString());
    }

    [Fact]
    public void Pagination_preserves_subcategory_and_filters()
    {
        var model = FilteredCatalog();

        var query = QueryHelpers.ParseQuery(CatalogQueryString.ForPage(model, 3));

        Assert.Equal(model.ActiveSubSlug, query["sub"].ToString());
        Assert.Equal(model.SearchQuery, query["q"].ToString());
        Assert.Equal(model.Sort, query["sort"].ToString());
        Assert.Equal("40", query["pageSize"].ToString());
        Assert.Equal("100.25", query["minPrice"].ToString());
        Assert.Equal("999.99", query["maxPrice"].ToString());
        Assert.Equal("3", query["page"].ToString());
    }

    [Theory]
    [InlineData("bg-BG")]
    [InlineData("en-US")]
    public void Price_filters_keep_cents_independently_of_display_culture(string culture)
    {
        using var scope = new CultureScope(culture);

        var query = QueryHelpers.ParseQuery(CatalogQueryString.ForPage(FilteredCatalog(), 2));

        Assert.Equal("100.25", query["minPrice"].ToString());
        Assert.Equal("999.99", query["maxPrice"].ToString());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    public void First_page_with_default_filters_has_an_explicit_query(int page)
    {
        var model = new CatalogViewModel { Page = 2 };

        Assert.Equal("?page=1", CatalogQueryString.ForPage(model, page));
    }

    private static CatalogViewModel FilteredCatalog() => new()
    {
        ActiveSubSlug = "divani",
        SearchQuery = "диван & маса+стол",
        Sort = "price_asc",
        Page = 5,
        PageSize = 40,
        MinPrice = 100.25m,
        MaxPrice = 999.99m
    };
}
