using Microsoft.AspNetCore.Mvc;

namespace Algara.Web.Controllers
{
    public class OrderController : Controller
    {
        // GET /Order
        public IActionResult Index()
        {
            return View();
        }
    }
}
