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

            // Използваме email за потребителско име в този пример.
            bool created = await _userService.RegisterUserAsync(model.Email, model.Email, model.Password);

            if (created)
            {
                // Ако регистрацията е успешна, вземаме потребителя и го вписваме стандартно.
                var user = await _userService.GetUserByUsernameAsync(model.Email);
                if (user != null)
                {
                    await _userService.SignInAsync(HttpContext, user, rememberMe: false);
                    return RedirectToAction("Index", "Home");
                }
            }

            ModelState.AddModelError(string.Empty, "Регистрацията не бе успешна.");
            return View(model);
        }

        [HttpGet]
        public IActionResult Login()
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                return RedirectToAction("Index", "Home");
            }

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var user = await _userService.GetUserByUsernameAsync(model.Email);
            if (user == null)
            {
                ModelState.AddModelError(string.Empty, "Грешен имейл или парола.");
                return View(model);
            }

            if (user.LockoutUntil.HasValue && user.LockoutUntil.Value > DateTime.Now)
            {
                ModelState.AddModelError("", $"Акаунтът ви е заключен до {user.LockoutUntil.Value.ToLocalTime()}.");
                return View(model);
            }

            if (!await _userService.ValidateUserAsync(model.Email, model.Password))
            {
                ModelState.AddModelError(string.Empty, "Грешен имейл или парола.");
                return View(model);
            }

            await _userService.SignInAsync(HttpContext, user, model.RememberMe, model.TimeZoneOffset);
            return RedirectToAction("Index", "Home");
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
    }
}