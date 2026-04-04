using System.Diagnostics;
using Algara.Data.Repositories;
using Algara.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace Algara.Web.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly IProductRepository _productRepository;
        private readonly ICategoryRepository _categoryRepository;

        public HomeController(
            ILogger<HomeController> logger,
            IProductRepository productRepository,
            ICategoryRepository categoryRepository)
        {
            _logger = logger;
            _productRepository = productRepository;
            _categoryRepository = categoryRepository;
        }

        public async Task<IActionResult> Index()
        {
            _logger.LogInformation("Index page accessed.");
            var model = new HomeViewModel
            {
                Categories       = await _categoryRepository.GetAllAsync(),
                FeaturedProducts = await _productRepository.GetFeaturedAsync(4),
            };
            return View(model);
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [Route("Home/HttpError/{code:int}")]
        public IActionResult HttpError(int code)
        {
            Response.StatusCode = code;
            return View(code);
        }

        public IActionResult About()
        {
            return View();
        }

        public IActionResult Contact()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
