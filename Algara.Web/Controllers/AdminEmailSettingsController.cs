using Algara.Identity.Authorization;
using Algara.Web.Authorization;
using Algara.Web.Services.Email;
using Algara.Web.ViewModels.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Algara.Web.Controllers;

[Authorize(Policy = RequirePermissionAttribute.StaffPolicy)]
[RequirePermission(PermissionCatalog.Settings.Manage)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("admin/settings/email")]
public sealed class AdminEmailSettingsController(SmtpSettingsService settings) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var snapshot = await settings.GetAsync(HttpContext.RequestAborted);
        return View(ToForm(snapshot));
    }

    [HttpPost(""), ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(SmtpSettingsForm form)
    {
        try
        {
            if (ModelState.IsValid)
            {
                var update = new SmtpSettingsUpdate
                {
                    Host = form.Host, Port = form.Port, SecurityMode = form.SecurityMode,
                    Username = form.Username, FromEmail = form.FromEmail, FromName = form.FromName ?? string.Empty,
                    PublicSiteUrl = form.PublicSiteUrl,
                    Enabled = form.Enabled, ExpectedVersion = form.ExpectedVersion,
                    Password = form.Password, RemovePassword = form.RemovePassword
                };
                var result = await settings.SaveAsync(User, update, HttpContext.RequestAborted);
                if (result.Success)
                {
                    TempData["Success"] = result.Changed ? "SMTP настройките са записани." : "Няма промени в SMTP настройките.";
                    return RedirectToAction(nameof(Index));
                }
                ModelState.AddModelError(string.Empty, result.Error ?? "SMTP настройките не са записани.");
            }
        }
        finally
        {
            ClearPassword(form);
        }
        // Refresh only saved-secret status/history. A stale submitted version must remain stale
        // until the administrator deliberately reloads the settings.
        ApplySavedStatus(form, await settings.GetAsync(HttpContext.RequestAborted));
        return View(form);
    }

    [HttpPost("check-connection"), ValidateAntiForgeryToken]
    public async Task<IActionResult> CheckConnection(SmtpConnectionCheckForm form)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Липсва валидна версия на настройките. Презаредете страницата.";
            return RedirectToAction(nameof(Index));
        }
        var result = await settings.CheckConnectionAsync(User, form.ExpectedVersion, HttpContext.RequestAborted);
        TempData[result.Success ? "Success" : "Error"] = result.Success
            ? "Защитената SMTP връзка и удостоверяването са успешни. Не е изпратено писмо."
            : result.Error ?? "SMTP връзката не е потвърдена.";
        return RedirectToAction(nameof(Index));
    }

    private void ClearPassword(SmtpSettingsForm form)
    {
        form.Password = null;
        var keys = ModelState.Keys.Where(key =>
            key.Equals(nameof(SmtpSettingsForm.Password), StringComparison.OrdinalIgnoreCase) ||
            key.EndsWith("." + nameof(SmtpSettingsForm.Password), StringComparison.OrdinalIgnoreCase)).ToList();
        var passwordHadErrors = keys.Any(key => ModelState[key]?.Errors.Count > 0);
        foreach (var key in keys) ModelState.Remove(key);
        if (passwordHadErrors)
            ModelState.AddModelError(string.Empty, "Паролата не е приета. Въведете я отново с до 1024 знака.");
    }

    private static SmtpSettingsForm ToForm(SmtpSettingsSnapshot snapshot)
    {
        var form = new SmtpSettingsForm
        {
            Host = snapshot.Host, Port = snapshot.Port, SecurityMode = snapshot.SecurityMode,
            Username = snapshot.Username, FromEmail = snapshot.FromEmail, FromName = snapshot.FromName,
            PublicSiteUrl = snapshot.PublicSiteUrl,
            Enabled = snapshot.Enabled, ExpectedVersion = snapshot.Version
        };
        ApplySavedStatus(form, snapshot);
        return form;
    }

    private static void ApplySavedStatus(SmtpSettingsForm form, SmtpSettingsSnapshot snapshot)
    {
        form.HasPassword = snapshot.HasPassword;
        form.PasswordReadable = snapshot.PasswordReadable;
        form.History = snapshot.History;
        form.CanCheckConnection = snapshot.Version != "none" && snapshot.HasPassword && snapshot.PasswordReadable;
        form.DeliverySetupWarning = !SmtpSettingsRules.TryPublicSiteUrl(snapshot.PublicSiteUrl, out _)
            ? "В запазените настройки липсва валиден публичен адрес на сайта. Попълнете го и запазете, за да се изпращат покани, потвърждения и връзки за нова парола."
            : !snapshot.Enabled
                ? "Изпращането е изключено в запазените настройки. Включете „Разреши използването на SMTP“ и запазете."
                : !snapshot.HasPassword || !snapshot.PasswordReadable
                    ? "За изпращане на писма въведете и запазете използваема SMTP парола."
                    : null;
    }
}
