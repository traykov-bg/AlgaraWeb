using Algara.Identity.Models;
using Algara.Identity.Services; // нашата дефиниция на IUserService
using Algara.Web.ViewModels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Algara.Web.Controllers
{
    public class AccountController : Controller
    {
        private readonly IUserService _userService;

        public AccountController(IUserService userService)
        {
            _userService = userService;
        }

        [HttpGet]
        public IActionResult Register()
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                return RedirectToAction("Index", "Home");
            }

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var data = new RegistrationData
            {
                Email = model.Email,
                Password = model.Password,
                FirstName = model.FirstName.Trim(),
                LastName = model.LastName.Trim(),
                PhoneNumber = model.PhoneNumber,
                MarketingConsent = model.MarketingConsent,
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                UserAgent = Request.Headers.UserAgent.ToString()
            };

            var user = await _userService.RegisterUserAsync(data);
            if (user == null)
            {
                ModelState.AddModelError(string.Empty, "Имейлът вече е регистриран.");
                return View(model);
            }

            await _userService.AddUserToRoleAsync(user.UserName, "User");
            await _userService.SignInAsync(HttpContext, user, rememberMe: false);
            return RedirectToAction("Index", "Home");
        }

        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                return RedirectToLocal(returnUrl);
            }

            ViewBag.ReturnUrl = returnUrl;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
        {
            bool isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest";

            if (!ModelState.IsValid)
                return LoginFailure(model, returnUrl, isAjax);

            var user = await _userService.GetUserByUsernameAsync(model.Email);
            if (user == null)
            {
                ModelState.AddModelError(string.Empty, "Грешен имейл или парола.");
                return LoginFailure(model, returnUrl, isAjax);
            }

            if (user.LockoutUntil.HasValue && user.LockoutUntil.Value > DateTime.Now)
            {
                ModelState.AddModelError("", $"Акаунтът ви е заключен до {user.LockoutUntil.Value.ToLocalTime()}.");
                return LoginFailure(model, returnUrl, isAjax);
            }

            if (!await _userService.ValidateUserAsync(model.Email, model.Password))
            {
                ModelState.AddModelError(string.Empty, "Грешен имейл или парола.");
                return LoginFailure(model, returnUrl, isAjax);
            }

            await _userService.SignInAsync(HttpContext, user, model.RememberMe, model.TimeZoneOffset);

            if (isAjax)
            {
                var target = !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)
                    ? returnUrl
                    : Url.Action("Index", "Home");
                return Json(new { success = true, redirectUrl = target });
            }

            return RedirectToLocal(returnUrl);
        }

        [HttpGet]
        public IActionResult ForgotPassword()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await _userService.SignOutAsync(HttpContext);
            return RedirectToAction("Index", "Home");
        }

        [Authorize]
        public IActionResult ClaimsInfo()
        {
            var claims = User.Claims.Select(c => new { c.Type, c.Value }).ToList();
            return Json(claims);
        }

        // AdminPanel stub премахнат — вместо него AdminController на /admin/

        [HttpGet]
        public IActionResult AccessDenied()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize]
        public async Task<IActionResult> ForceLogoutAll()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
            await _userService.ForceSignOutAllSessionsAsync(userId);
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login", "Account");
        }

        // ─── Helpers ───────────────────────────────────────────────────────────
        private IActionResult RedirectToLocal(string? returnUrl)
        {
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);

            return RedirectToAction("Index", "Home");
        }

        private IActionResult LoginFailure(LoginViewModel model, string? returnUrl, bool isAjax)
        {
            if (isAjax)
            {
                var errors = ModelState
                    .Where(kvp => kvp.Value?.Errors.Count > 0)
                    .ToDictionary(
                        kvp => kvp.Key,
                        kvp => kvp.Value!.Errors.Select(e => e.ErrorMessage).ToArray());
                return Json(new { success = false, errors });
            }

            ViewBag.ReturnUrl = returnUrl;
            return View(model);
        }
    }
}
