using Algara.Data.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace Algara.Web.Controllers
{
    public class ProductController : Controller
    {
        private readonly IProductRepository _productRepository;
        private readonly ICategoryRepository _categoryRepository;
        private readonly ILogger<ProductController> _logger;

        public ProductController(
            IProductRepository productRepository,
            ICategoryRepository categoryRepository,
            ILogger<ProductController> logger)
        {
            _productRepository = productRepository;
            _categoryRepository = categoryRepository;
            _logger = logger;
        }

        // GET /Product  или  /Product?q=диван&sort=newest
        public async Task<IActionResult> Index(string? q = null, string? sort = null)
        {
            var allProducts = await _productRepository.GetAllAsync();

            IEnumerable<Algara.Data.Models.Product> products;
            if (!string.IsNullOrWhiteSpace(q))
            {
                products = allProducts.Where(p =>
                    p.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    (p.Category?.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (p.Description?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false));
                ViewBag.SearchQuery = q;
            }
            else
            {
                products = allProducts;
            }

            products = ApplySort(products, sort);
            ViewBag.Sort = sort ?? "newest";
            ViewBag.Categories = await _categoryRepository.GetAllAsync();
            return View(products);
        }

        // GET /kategorii/{slug}   напр. /kategorii/meka-mebel
        // GET /kategorii/{slug}?sub={subSlug}   напр. /kategorii/meka-mebel?sub=sofas
        [Route("/kategorii/{slug}")]
        public async Task<IActionResult> Category(string slug, string? sub = null, string? sort = null)
        {
            var category = await _categoryRepository.GetBySlugWithSubCategoriesAsync(slug);
            if (category == null) return NotFound();

            IEnumerable<Algara.Data.Models.Product> products;
            Algara.Data.Models.SubCategory? activeSubCategory = null;

            if (!string.IsNullOrEmpty(sub))
            {
                activeSubCategory = category.SubCategories
                    .FirstOrDefault(sc => sc.Slug == sub && sc.IsActive);

                products = activeSubCategory != null
                    ? await _productRepository.GetBySubCategoryAsync(activeSubCategory.N)
                    : await _productRepository.GetByCategoryAsync(category.N);
            }
            else
            {
                products = await _productRepository.GetByCategoryAsync(category.N);
            }

            products = ApplySort(products, sort);
            ViewBag.Sort          = sort ?? "newest";
            ViewBag.Categories    = await _categoryRepository.GetAllAsync();
            ViewBag.CategoryName  = category.Name;
            ViewBag.CategorySlug  = category.Slug;
            ViewBag.SubCategories = category.SubCategories.Where(sc => sc.IsActive).OrderBy(sc => sc.Name).ToList();
            ViewBag.ActiveSubSlug = activeSubCategory?.Slug;
            return View("Index", products);
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

        private static IEnumerable<Algara.Data.Models.Product> ApplySort(
            IEnumerable<Algara.Data.Models.Product> products, string? sort) => sort switch
        {
            "name_asc"  => products.OrderBy(p => p.Name),
            "name_desc" => products.OrderByDescending(p => p.Name),
            "price_asc" => products.OrderBy(p => p.Price),
            "price_desc"=> products.OrderByDescending(p => p.Price),
            _           => products.OrderByDescending(p => p.CreatedAt), // newest (default)
        };

        // GET /Product/Detail/{n}
        public async Task<IActionResult> Detail(int n)
        {
            var product = await _productRepository.GetByNAsync(n);
            if (product == null) return NotFound();
            return View(product);
        }
    }
}
