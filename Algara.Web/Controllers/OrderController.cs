using Microsoft.AspNetCore.Mvc;

namespace Algara.Web.Controllers
{
    using Algara.Web.Services.Cart;

    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public class OrderController(CartService cart) : Controller
    {
        [HttpGet]
        public async Task<IActionResult> Index() => View(await cart.GetAsync());

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Add(int productN, int? variantN, int quantity = 1)
        {
            var result = ModelState.IsValid ? await cart.AddAsync(productN, variantN, quantity) : new(CartLineIssue.InvalidQuantity);
            TempData["CartMessage"] = result.Success ? "Added" : "Issue" + result.Issue;
            return RedirectToAction(nameof(Index));
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Update(int productN, int? variantN, int quantity)
        {
            var result = ModelState.IsValid ? await cart.UpdateAsync(productN, variantN, quantity) : new(CartLineIssue.InvalidQuantity);
            TempData["CartMessage"] = result.Success ? "Updated" : "Issue" + result.Issue;
            return RedirectToAction(nameof(Index));
        }

        [HttpPost, ValidateAntiForgeryToken]
        public IActionResult Remove(int productN, int? variantN)
        {
            if (ModelState.IsValid) cart.Remove(productN, variantN);
            return RedirectToAction(nameof(Index));
        }

        [HttpPost, ValidateAntiForgeryToken]
        public IActionResult Clear()
        {
            cart.Clear();
            return RedirectToAction(nameof(Index));
        }
    }
}
