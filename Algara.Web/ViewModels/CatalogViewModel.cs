using Algara.Data.Models;

namespace Algara.Web.ViewModels
{
    public class CatalogViewModel
    {
        public IReadOnlyList<Product> Products { get; init; } = [];
        public int TotalCount  { get; init; }
        public int Page        { get; init; } = 1;
        public int PageSize    { get; init; } = 20;
        public int TotalPages  => TotalCount == 0 ? 1 : (int)Math.Ceiling((double)TotalCount / PageSize);

        public string?  SearchQuery   { get; init; }
        public string   Sort          { get; init; } = "newest";
        public decimal? MinPrice      { get; init; }
        public decimal? MaxPrice      { get; init; }
        public decimal  RangeMin      { get; init; }
        public decimal  RangeMax      { get; init; }

        public IEnumerable<Category>    Categories    { get; init; } = [];
        public List<SubCategory>?       SubCategories { get; init; }
        public string?                  CategorySlug  { get; init; }
        public string?                  CategoryName  { get; init; }
        public string?                  ActiveSubSlug { get; init; }

        public static readonly int[] PageSizeOptions = [20, 40, 60, 80, 100];
    }
}
