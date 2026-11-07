using System.Security.Claims;
using Algara.Data.Data;
using Algara.Identity.Data;
using Algara.Web.Localization;
using Algara.Web.Services.Cart;
using Algara.Web.Services.Checkout;
using Algara.Web.Services.Email;
using Algara.Web.Services.Shipping;
using Algara.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace Algara.Web.Controllers;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CheckoutController(CartService cart, CheckoutService checkout, CheckoutTokens tokens,
    DeliveryQuoteService delivery, ShopDbContext db, IdentityDbContext identity,
    IStringLocalizer<CheckoutResources> text, IStringLocalizer<ShippingResources> shipping,
    IStringLocalizer<CatalogResources> catalog) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var form = new CheckoutForm { Cart = await cart.GetAsync(), SubmissionToken = tokens.Issue(CheckoutService.Owner(User)) };
        if (User.Identity?.IsAuthenticated == true)
        {
            var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var user = await identity.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id);
            if (user != null) { form.FirstName = user.FirstName ?? ""; form.LastName = user.LastName ?? ""; form.Email = user.Email; form.Phone = user.PhoneNumber ?? ""; }
        }
        form.EnabledCouriers = await EnabledCouriersAsync();
        return View(form);
    }

    [HttpPost, ValidateAntiForgeryToken, EnableRateLimiting("DeliveryQuotes")]
    public async Task<IActionResult> Quote(DeliveryQuoteForm form, string? submissionToken)
    {
        var ticket = tokens.Read(submissionToken, CheckoutService.Owner(User));
        if (ticket == null) return BadRequest(new { error = text["Expired"].Value });
        if (!ModelState.IsValid) return BadRequest(new { error = text["InvalidDetails"].Value });
        var snapshot = await cart.GetAsync();
        var result = await delivery.CalculateAsync(snapshot, form.Method, form.City, form.PostalCode, form.Address, form.OfficeCode, HttpContext.RequestAborted);
        if (result.Quote == null) return Json(new { error = shipping[result.ErrorKey ?? "InvalidCart"].Value });
        var quote = result.Quote;
        var fingerprint = CheckoutTokens.Fingerprint(snapshot, new CheckoutForm { Method = form.Method, City = form.City, PostalCode = form.PostalCode, Address = form.Address, OfficeCode = form.OfficeCode }, quote);
        return Json(new { token = quote.IsTest ? null : tokens.Quote(ticket, fingerprint), goods = quote.GoodsAmount.ToString("N2"), delivery = quote.DeliveryAmount.ToString("N2"), total = quote.Total.ToString("N2"),
            lines = snapshot.Lines.Select(l => new { name = l.Name, variant = l.VariantName, quantity = l.Entry.Quantity,
                price = l.UnitPrice!.Value.ToString("N2"), availability = catalog["Availability" + l.Availability].Value }),
            address = quote.ResolvedAddress ?? (quote.Office == null ? "" : quote.Office.Name + " — " + quote.Office.Address), google = quote.DistanceKm.HasValue,
            error = quote.IsTest ? text["TestNotOrder"].Value : null });
    }

    [HttpPost, ValidateAntiForgeryToken, EnableRateLimiting("DeliveryQuotes")]
    public async Task<IActionResult> Submit(CheckoutForm form)
    {
        CheckoutResult result = ModelState.IsValid ? await checkout.SubmitAsync(form, User, HttpContext.RequestAborted) : new(ErrorKey: "InvalidDetails");
        form.Password = form.ConfirmPassword = null;
        ModelState.Remove(nameof(form.Password)); ModelState.Remove(nameof(form.ConfirmPassword));
        if (result.Order != null)
        {
            SetAccess(result.GuestSecret!);
            var current = await cart.GetAsync();
            if (current.Lines.Count == result.Order.OrderItems.Count && current.Lines.All(l => result.Order.OrderItems.Any(i =>
                i.ProductN == l.Entry.ProductN && i.VariantN == l.Entry.VariantN && i.Quantity == l.Entry.Quantity))) cart.Clear();
            return RedirectToAction(nameof(Details), new { n = result.Order.N });
        }
        form.EnabledCouriers = await EnabledCouriersAsync();
        form.Cart = await cart.GetAsync();
        if (DeliveryQuoteService.IsOffice(form.Method) && form.Cart.CanQuote)
        {
            var offices = await delivery.OfficesAsync(form.Cart, form.Method, form.City, HttpContext.RequestAborted);
            if (offices.Success) form.Offices = offices.Offices;
        }
        form.ErrorKey = result.ErrorKey ?? "Retry";
        form.QuoteToken = null; ModelState.Remove(nameof(form.QuoteToken));
        return View("Index", form);
    }

    [HttpGet, ActionName(nameof(Submit))]
    public IActionResult SubmitPage() => RedirectToAction(nameof(Index));

    [HttpGet]
    public IActionResult Access()
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        return View();
    }

    [HttpPost, ValidateAntiForgeryToken, EnableRateLimiting("AccountEmail")]
    public async Task<IActionResult> Access(string? token)
    {
        if (!AccountEmailRules.TryParse(token, out var id, out var secret)) return NotFound();
        var order = await db.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.SubmissionId == id);
        if (order?.GuestTokenHash == null || !AccountEmailRules.MatchesHash(secret, order.GuestTokenHash)) return NotFound();
        SetAccess(token!);
        return RedirectToAction(nameof(Details), new { n = order.N });
    }

    [HttpGet]
    public async Task<IActionResult> Details(int n)
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        var order = await db.Orders.AsNoTracking().Include(o => o.OrderItems).SingleOrDefaultAsync(o => o.N == n);
        if (order == null) return NotFound();
        var owner = CheckoutService.Owner(User);
        var allowed = owner != "guest" && order.UserN != null && await identity.Users.AnyAsync(u => u.N == order.UserN && u.Id == owner);
        if (!allowed && order.SubmissionId != null && AccountEmailRules.TryParse(Request.Cookies["AlgaraOrder_" + order.SubmissionId.Value.ToString("N")], out var id, out var secret))
            allowed = id == order.SubmissionId && order.GuestTokenHash != null && AccountEmailRules.MatchesHash(secret, order.GuestTokenHash);
        return allowed ? View(order) : NotFound();
    }

    [HttpPost, ValidateAntiForgeryToken, Microsoft.AspNetCore.Authorization.Authorize]
    public async Task<IActionResult> Link(int n, [FromServices] GuestOrderLinkService linking)
    {
        var submission = await db.Orders.Where(o => o.N == n).Select(o => o.SubmissionId).SingleOrDefaultAsync();
        if (submission == null || !await linking.LinkAsync(n, Request.Cookies["AlgaraOrder_" + submission.Value.ToString("N")], User, HttpContext.RequestAborted)) return NotFound();
        return RedirectToAction(nameof(Details), new { n });
    }

    private Task<List<int>> EnabledCouriersAsync() => identity.CourierConfigurations.AsNoTracking()
        .Where(c => c.Enabled).Select(c => c.Id).ToListAsync(HttpContext.RequestAborted);

    private void SetAccess(string secret)
    {
        AccountEmailRules.TryParse(secret, out var id, out _);
        Response.Cookies.Append("AlgaraOrder_" + id.ToString("N"), secret, new CookieOptions { HttpOnly = true, Secure = Request.IsHttps,
            SameSite = SameSiteMode.Lax, Path = "/Checkout", MaxAge = TimeSpan.FromDays(30), IsEssential = true });
    }
}
