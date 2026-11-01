using Algara.Identity.Authorization;
using Algara.Web.Authorization;
using Algara.Web.Services.Shipping;
using Algara.Web.ViewModels.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Algara.Web.Controllers;

[Authorize(Policy = RequirePermissionAttribute.StaffPolicy)]
[RequirePermission(PermissionCatalog.Settings.Manage)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("admin/settings/delivery")]
public sealed class AdminDeliverySettingsController(DeliverySettingsService settings) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index() => View(await settings.GetFormAsync(HttpContext.RequestAborted));

    [HttpPost(""), ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(DeliverySettingsForm form)
    {
        if (form.Econt == null || form.Speedy == null || form.Transport == null)
        {
            ModelState.AddModelError(string.Empty, "Липсва част от настройките за доставка. Презаредете страницата.");
            form.Econt ??= new();
            form.Speedy ??= new();
            form.Transport ??= new();
        }
        try
        {
            if (ModelState.IsValid)
            {
                var error = await settings.SaveAsync(User, form, HttpContext.RequestAborted);
                if (error == null)
                {
                    TempData["Success"] = "Настройките за доставка са записани.";
                    return RedirectToAction(nameof(Index));
                }
                ModelState.AddModelError(string.Empty, error);
            }
        }
        finally
        {
            form.Econt.Password = null;
            form.Speedy.Password = null;
            form.Transport.GeocodingKey = null;
            foreach (var key in ModelState.Keys.Where(k => k.EndsWith(".Password", StringComparison.OrdinalIgnoreCase) ||
                         k.EndsWith(".GeocodingKey", StringComparison.OrdinalIgnoreCase)).ToArray())
            {
                var hadError = ModelState[key]?.Errors.Count > 0;
                ModelState.Remove(key);
                if (hadError) ModelState.AddModelError(string.Empty, "Проверете и въведете отново API паролата (до 1000 знака) или ключа (до 1024 знака).");
            }
        }
        var saved = await settings.GetFormAsync(HttpContext.RequestAborted);
        form.Econt.HasReadablePassword = saved.Econt.HasReadablePassword;
        form.Speedy.HasReadablePassword = saved.Speedy.HasReadablePassword;
        form.Transport.HasReadableKey = saved.Transport.HasReadableKey;
        form.History = saved.History;
        return View(form);
    }
}
