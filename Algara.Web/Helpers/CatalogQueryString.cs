using System.Globalization;
using Algara.Web.ViewModels;
using Microsoft.AspNetCore.Http;

namespace Algara.Web.Helpers;

public static class CatalogQueryString
{
    public static string ForPage(CatalogViewModel model, int page)
        => Build(model, page, model.ActiveSubSlug);

    // A null selection means all subcategories, independently of the active filter.
    public static string ForSubcategory(CatalogViewModel model, string? subcategory)
        => Build(model, 1, subcategory);

    private static string Build(CatalogViewModel model, int page, string? subcategory)
    {
        var values = new List<KeyValuePair<string, string?>>();

        void Add(string key, string? value)
        {
            if (!string.IsNullOrEmpty(value))
                values.Add(new(key, value));
        }

        Add("sub", subcategory);
        Add("q", model.SearchQuery);
        if (model.Sort != "newest") Add("sort", model.Sort);
        if (model.PageSize != 20) Add("pageSize", model.PageSize.ToString(CultureInfo.InvariantCulture));
        Add("minPrice", model.MinPrice?.ToString(CultureInfo.InvariantCulture));
        Add("maxPrice", model.MaxPrice?.ToString(CultureInfo.InvariantCulture));

        // An empty href would retain the current query instead of returning to page 1.
        Add("page", Math.Max(1, page).ToString(CultureInfo.InvariantCulture));
        return QueryString.Create(values).ToString();
    }
}
