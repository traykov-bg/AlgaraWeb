using Algara.Data.Models;
using Algara.Data.Repositories;
using Algara.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace Algara.Web.Controllers
{
    public class ProductController : Controller
    {
        private readonly IProductRepository  _productRepository;
        private readonly ICategoryRepository _categoryRepository;
        private readonly ILogger<ProductController> _logger;

        public ProductController(
            IProductRepository productRepository,
            ICategoryRepository categoryRepository,
            ILogger<ProductController> logger)
        {
            _productRepository  = productRepository;
            _categoryRepository = categoryRepository;
            _logger             = logger;
        }

        // GET /Product  или  /Product?q=диван&sort=newest&page=2&pageSize=40&minPrice=100&maxPrice=500
        public async Task<IActionResult> Index(
            string? q        = null,
            string? sort     = null,
            int     page     = 1,
            int     pageSize = 20,
            decimal? minPrice = null,
            decimal? maxPrice = null)
        {
            var all = await _productRepository.GetAllAsync();

            IEnumerable<Product> filtered = all;
            if (!string.IsNullOrWhiteSpace(q))
                filtered = filtered.Where(p =>
                    p.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    (p.Category?.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (p.Description?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false));

            var now = DateTime.Now;
            var vm = BuildViewModel(filtered, now,
                page: page, pageSize: pageSize,
                sort: sort, minPrice: minPrice, maxPrice: maxPrice,
                searchQuery: q,
                categories: await _categoryRepository.GetAllAsync());

            return View(vm);
        }

        // GET /kategorii/{slug}   напр. /kategorii/meka-mebel?sub=sofas&sort=newest&page=1&pageSize=40
        [Route("/kategorii/{slug}")]
        public async Task<IActionResult> Category(
            string  slug,
            string? sub      = null,
            string? sort     = null,
            int     page     = 1,
            int     pageSize = 20,
            decimal? minPrice = null,
            decimal? maxPrice = null)
        {
            var category = await _categoryRepository.GetBySlugWithSubCategoriesAsync(slug);
            if (category == null) return NotFound();

            SubCategory? activeSubCategory = null;
            IEnumerable<Product> filtered;

            if (!string.IsNullOrEmpty(sub))
            {
                activeSubCategory = category.SubCategories
                    .FirstOrDefault(sc => sc.Slug == sub && sc.IsActive);

                filtered = activeSubCategory != null
                    ? await _productRepository.GetBySubCategoryAsync(activeSubCategory.N)
                    : await _productRepository.GetByCategoryAsync(category.N);
            }
            else
            {
                filtered = await _productRepository.GetByCategoryAsync(category.N);
            }

            var now = DateTime.Now;
            var vm = BuildViewModel(filtered, now,
                page: page, pageSize: pageSize,
                sort: sort, minPrice: minPrice, maxPrice: maxPrice,
                categories: await _categoryRepository.GetAllAsync(),
                categorySlug: category.Slug,
                categoryName: category.Name,
                subCategories: category.SubCategories.Where(sc => sc.IsActive).OrderBy(sc => sc.Name).ToList(),
                activeSubSlug: activeSubCategory?.Slug);

            return View("Index", vm);
        }

        // GET /Product/Search?q=диван  (JSON — за live search dropdown)
        [HttpGet]
        public async Task<IActionResult> Search(string? q)
        {
            if (string.IsNullOrWhiteSpace(q) || q.Length < 2)
                return Json(Array.Empty<object>());

            var allProducts = await _productRepository.GetAllAsync();
            var results = allProducts
                .Where(p =>
                    p.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    (p.Category?.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (p.Description?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false))
                .Take(5)
                .Select(p => new
                {
                    n        = p.N,
                    name     = p.Name,
                    category = p.Category?.Name,
                    price    = (long)p.Price,
                    imageUrl = p.ImageUrl,
                });

            return Json(results);
        }

        // GET /Product/Detail/{n}
        public async Task<IActionResult> Detail(int n)
        {
            var product = await _productRepository.GetByNAsync(n);
            if (product == null) return NotFound();
            return View(product);
        }

        // ── helpers ──────────────────────────────────────────────────────────

        private static CatalogViewModel BuildViewModel(
            IEnumerable<Product> source,
            DateTime             now,
            int                  page,
            int                  pageSize,
            string?              sort,
            decimal?             minPrice,
            decimal?             maxPrice,
            IEnumerable<Category> categories,
            string?              searchQuery   = null,
            string?              categorySlug  = null,
            string?              categoryName  = null,
            List<SubCategory>?   subCategories = null,
            string?              activeSubSlug = null)
        {
            pageSize = CatalogViewModel.PageSizeOptions.Contains(pageSize) ? pageSize : CatalogViewModel.PageSizeOptions[0];
            page     = page < 1 ? 1 : page;

            // Изчисляваме ефективната цена (след промоции) за всеки продукт веднъж
            var withPrice = source
                .Select(p => (product: p, effective: p.GetDiscountedPrice(now)))
                .ToList();

            // Диапазон за целия (нефилтриран по цена) резултат
            var rangeMin = withPrice.Count > 0 ? (decimal)Math.Floor((double)withPrice.Min(x => x.effective)) : 0m;
            var rangeMax = withPrice.Count > 0 ? (decimal)Math.Ceiling((double)withPrice.Max(x => x.effective)) : 0m;

            // Филтър по цена
            if (minPrice.HasValue) withPrice = withPrice.Where(x => x.effective >= minPrice.Value).ToList();
            if (maxPrice.HasValue) withPrice = withPrice.Where(x => x.effective <= maxPrice.Value).ToList();

            // Сортиране
            var sorted = ApplySort(withPrice.Select(x => x.product), sort);

            var totalCount = sorted.Count();
            var products   = sorted.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            return new CatalogViewModel
            {
                Products      = products,
                TotalCount    = totalCount,
                Page          = page,
                PageSize      = pageSize,
                Sort          = sort ?? "newest",
                MinPrice      = minPrice,
                MaxPrice      = maxPrice,
                RangeMin      = rangeMin,
                RangeMax      = rangeMax,
                SearchQuery   = searchQuery,
                Categories    = categories,
                CategorySlug  = categorySlug,
                CategoryName  = categoryName,
                SubCategories = subCategories,
                ActiveSubSlug = activeSubSlug,
            };
        }

        private static IEnumerable<Product> ApplySort(IEnumerable<Product> products, string? sort) => sort switch
        {
            "name_asc"   => products.OrderBy(p => p.Name),
            "name_desc"  => products.OrderByDescending(p => p.Name),
            "price_asc"  => products.OrderBy(p => p.Price),
            "price_desc" => products.OrderByDescending(p => p.Price),
            _            => products.OrderByDescending(p => p.CreatedAt),
        };
    }
}
