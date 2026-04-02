using System.Diagnostics;
using Algara.Web.Models;
using Algara.Web.Repositories;
using Algara.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace Algara.Web.Controllers
{
    public class HomeController : Controller
    {
        private readonly IProductRepository _productRepository;
        private readonly ILogger<HomeController> _logger;

        public HomeController(IProductRepository productRepository,ILogger<HomeController> logger)
        {
            _productRepository = productRepository;
            _logger = logger;
        }

        public IActionResult Index()
        {
            _logger.LogInformation("Index page accessed.");
            return View();
        }

        public async Task<IActionResult> TestProducts()
        {
            var products = await _productRepository.GetAllAsync();
            return Json(products);
        }

        public IActionResult Privacy()
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
