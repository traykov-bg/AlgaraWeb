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

        // GET /Product
        public async Task<IActionResult> Index()
        {
            var products = await _productRepository.GetAllAsync();
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

        // GET /Product/Detail/5
        public async Task<IActionResult> Detail(int n)
        {
            var product = await _productRepository.GetByNAsync(n);
            if (product == null) return NotFound();
            return View(product);
        }
    }
}
