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

        // GET /Product  или  /Product?q=диван
        public async Task<IActionResult> Index(string? q = null)
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

            ViewBag.Categories = await _categoryRepository.GetAllAsync();
            return View(products);
        }

        // GET /Product/Category/5
        public async Task<IActionResult> Category(int n)
        {
            var category = await _categoryRepository.GetByNAsync(n);
            if (category == null) return NotFound();

            var products = await _productRepository.GetByCategoryAsync(n);
            ViewBag.Categories = await _categoryRepository.GetAllAsync();
            ViewBag.CategoryName = category.Name;
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

        // GET /Product/Detail/5
        public async Task<IActionResult> Detail(int n)
        {
            var product = await _productRepository.GetByNAsync(n);
            if (product == null) return NotFound();
            return View(product);
        }
    }
}
