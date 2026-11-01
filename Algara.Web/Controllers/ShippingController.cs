using Algara.Web.Services.Cart;
using Algara.Web.Services.Shipping;
using Algara.Web.Services.Shipping.Carriers;
using Algara.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Algara.Web.Controllers;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ShippingController(CartService cart, DeliveryQuoteService quotes) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index() => View(new DeliveryQuoteForm { Cart = await cart.GetAsync() });

    // A language switch after a POST result follows its current URL with GET.
    // Return to the blank preview without repeating external calls or carrying address data.
    [HttpGet, ActionName(nameof(Calculate))]
    public IActionResult CalculatePage() => RedirectToAction(nameof(Index));

    [HttpGet, ActionName(nameof(Offices))]
    public IActionResult OfficesPage() => RedirectToAction(nameof(Index));

    [HttpPost, ValidateAntiForgeryToken, EnableRateLimiting("DeliveryQuotes")]
    public async Task<IActionResult> Cities(DeliveryMethod method)
    {
        var result = await quotes.CitiesAsync(await cart.GetAsync(), method, HttpContext.RequestAborted);
        if (!result.Success) return StatusCode(503);
        return Json(result.Offices.Select(o => o.City).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToArray());
    }

    [HttpPost, ValidateAntiForgeryToken, EnableRateLimiting("DeliveryQuotes")]
    public async Task<IActionResult> Calculate(DeliveryQuoteForm form)
    {
        form.Quote = null;
        form.Offices = [];
        form.ErrorKey = null;
        form.Cart = await cart.GetAsync();
        if (ModelState.IsValid)
        {
            var result = await quotes.CalculateAsync(form.Cart, form.Method, form.City, form.PostalCode, form.Address, form.OfficeCode, HttpContext.RequestAborted);
            form.Quote = result.Quote;
            form.ErrorKey = result.ErrorKey;
        }
        else form.ErrorKey = "InvalidAddress";
        return View("Index", form);
    }

    [HttpPost, ValidateAntiForgeryToken, EnableRateLimiting("DeliveryQuotes")]
    public async Task<IActionResult> Offices(DeliveryQuoteForm form, bool json = false)
    {
        form.Quote = null;
        form.Offices = [];
        form.ErrorKey = null;
        form.OfficeCode = null;
        // This request needs a city only; hidden address fields and the previous
        // office selection must not prevent loading a fresh list of offices.
        ModelState.Remove(nameof(form.OfficeCode));
        ModelState.Remove(nameof(form.PostalCode));
        ModelState.Remove(nameof(form.Address));
        form.Cart = await cart.GetAsync();
        if (!form.Cart.CanQuote) form.ErrorKey = "InvalidCart";
        else if (!Enum.IsDefined(form.Method) || !DeliveryQuoteService.IsOffice(form.Method)) form.ErrorKey = "InvalidMethod";
        else if (!DeliveryQuoteService.AllowsCourier(form.Cart)) form.ErrorKey = "CourierNotAllowed";
        else if (!ModelState.IsValid) form.ErrorKey = "InvalidOfficeCity";
        else
        {
            var result = await quotes.OfficesAsync(form.Cart, form.Method, form.City, HttpContext.RequestAborted);
            form.Offices = result.Success ? result.Offices : [];
            form.ErrorKey = result.Success
                ? result.Offices.Count == 0 ? "NoOffices" : null
                : result.ErrorCode switch
                {
                    CourierErrorCode.NotConfigured => "OfficeLookupUnavailable",
                    CourierErrorCode.InvalidDestination => "InvalidOfficeCity",
                    CourierErrorCode.Rejected => "OfficeLookupRejected",
                    CourierErrorCode.InvalidResponse => "OfficeLookupInvalidResponse",
                    _ => "OfficeLookupFailed"
                };
        }
        if (json) return Json(new { offices = form.Offices, errorKey = form.ErrorKey });
        return View("Index", form);
    }
}
