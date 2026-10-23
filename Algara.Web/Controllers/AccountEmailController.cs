using Algara.Identity.Services;
using Algara.Web.Services.Email;
using Algara.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.RateLimiting;

namespace Algara.Web.Controllers;

[Route("Account")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AccountEmailController(IAccountEmailService accountEmail) : Controller
{
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        base.OnActionExecuting(context);
    }

    [HttpGet("ForgotPassword")]
    public IActionResult ForgotPassword() => View(new AccountEmailRequestForm());

    [HttpPost("ForgotPassword"), ValidateAntiForgeryToken, EnableRateLimiting("AccountEmail")]
    public async Task<IActionResult> ForgotPassword(AccountEmailRequestForm form)
    {
        if (!ModelState.IsValid) return View(form);
        await accountEmail.RequestResetAsync(form.Email, HttpContext.RequestAborted);
        return RedirectToAction(nameof(RequestAccepted));
    }

    [HttpGet("ResendConfirmation")]
    public IActionResult ResendConfirmation() => View(new AccountEmailRequestForm());

    [HttpPost("ResendConfirmation"), ValidateAntiForgeryToken, EnableRateLimiting("AccountEmail")]
    public async Task<IActionResult> ResendConfirmation(AccountEmailRequestForm form)
    {
        if (!ModelState.IsValid) return View(form);
        await accountEmail.RequestConfirmationAsync(form.Email, HttpContext.RequestAborted);
        return RedirectToAction(nameof(RequestAccepted));
    }

    [HttpGet("EmailRequestAccepted")]
    public IActionResult RequestAccepted() => View();

    [HttpGet("RegistrationPending")]
    public IActionResult RegistrationPending() => View();

    [HttpGet("ResetPassword")]
    public IActionResult ResetPassword() => View(new ResetPasswordForm());

    [HttpPost("ResetPassword"), ValidateAntiForgeryToken, EnableRateLimiting("AccountEmail")]
    public async Task<IActionResult> ResetPassword(ResetPasswordForm form)
    {
        if (ModelState.IsValid)
        {
            var result = await accountEmail.ResetPasswordAsync(form.Token, form.Password, HttpContext.RequestAborted);
            if (result.Success) return View("Completed", "Паролата е сменена. Влезте с новата парола.");
            ModelState.AddModelError(string.Empty, result.Error ?? "Връзката е невалидна или е изтекла. Поискайте ново писмо.");
        }
        ClearPasswords(form);
        return View(form);
    }

    [HttpGet("ConfirmEmail")]
    public IActionResult ConfirmEmail() => View(new AccountEmailTokenForm());

    [HttpPost("ConfirmEmail"), ValidateAntiForgeryToken, EnableRateLimiting("AccountEmail")]
    public async Task<IActionResult> ConfirmEmail(AccountEmailTokenForm form)
    {
        if (ModelState.IsValid)
        {
            var result = await accountEmail.ConfirmEmailAsync(form.Token, HttpContext.RequestAborted);
            if (result.Success) return View("Completed", "Имейлът е потвърден. Вече можете да влезете в акаунта си.");
            ModelState.AddModelError(string.Empty, result.Error ?? "Връзката е невалидна или е изтекла. Поискайте ново писмо.");
        }
        return View(form);
    }

    [HttpGet("AcceptInvitation")]
    public IActionResult AcceptInvitation() => View(new AcceptInvitationForm());

    [HttpPost("AcceptInvitation"), ValidateAntiForgeryToken, EnableRateLimiting("AccountEmail")]
    public async Task<IActionResult> AcceptInvitation(AcceptInvitationForm form)
    {
        if (ModelState.IsValid)
        {
            var data = new RegistrationData
            {
                FirstName = form.FirstName.Trim(), LastName = form.LastName.Trim(), PhoneNumber = form.PhoneNumber,
                Password = form.Password, MarketingConsent = form.MarketingConsent, TermsAccepted = form.TermsAccepted,
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(), UserAgent = Request.Headers.UserAgent.ToString()
            };
            var result = await accountEmail.AcceptInvitationAsync(form.Token, data, HttpContext.RequestAborted);
            if (result.Success) return View("Completed", "Акаунтът е създаден, имейлът е потвърден и поканата е приета. Вече можете да влезете.");
            ModelState.AddModelError(string.Empty, result.Error ?? "Поканата не може да бъде приета. Свържете се със служителя, който я изпрати.");
        }
        ClearPasswords(form);
        return View(form);
    }

    private void ClearPasswords(ResetPasswordForm form)
    {
        form.Password = form.ConfirmPassword = string.Empty;
        foreach (var key in new[] { nameof(form.Password), nameof(form.ConfirmPassword) })
            if (ModelState.TryGetValue(key, out var entry)) entry.RawValue = entry.AttemptedValue = null;
    }
}
