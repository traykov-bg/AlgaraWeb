using Algara.Data.Models;
using Algara.Data.Repositories;
using Algara.Identity.Data;
using Algara.Identity.Models;
using Algara.Identity.Services;
using Algara.Web.ViewModels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Algara.Web.Controllers
{
    public class AccountController : Controller
    {
        private readonly IUserService _userService;
        private readonly IdentityDbContext _identityDb;
        private readonly IOrderRepository _orderRepository;

        public AccountController(
            IUserService userService,
            IdentityDbContext identityDb,
            IOrderRepository orderRepository)
        {
            _userService = userService;
            _identityDb = identityDb;
            _orderRepository = orderRepository;
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
        [HttpGet]
        public async Task<IActionResult> Profile(string? tab = null)
        {
            var user = await GetCurrentUserAsync();
            if (user == null)
            {
                await _userService.SignOutAsync(HttpContext);
                return RedirectToAction(nameof(Login));
            }

            return View(await BuildProfileViewModelAsync(user, activeTab: tab));
        }

        [Authorize]
        [HttpGet]
        public async Task<IActionResult> OrderDetails(int n)
        {
            var user = await GetCurrentUserAsync();
            if (user == null)
                return RedirectToAction(nameof(Login));

            var order = await _orderRepository.GetByNForUserAsync(n, user.N);
            if (order == null)
                return NotFound();

            return View(new ProfileOrderDetailViewModel
            {
                Order = order,
                StatusLabel = StatusLabel(order.Status),
                StatusCssClass = StatusCssClass(order.Status)
            });
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateProfile([Bind(Prefix = "Details")] ProfileDetailsViewModel details)
        {
            var user = await GetCurrentUserAsync();
            if (user == null)
                return RedirectToAction(nameof(Login));

            if (!ModelState.IsValid)
                return View("Profile", await BuildProfileViewModelAsync(user, details: details));

            user.FirstName = details.FirstName.Trim();
            user.LastName = details.LastName.Trim();
            user.FullName = $"{user.FirstName} {user.LastName}".Trim();
            user.DisplayName = user.FullName;
            user.PhoneNumber = NormalizeOptional(details.PhoneNumber);

            await _identityDb.SaveChangesAsync();
            TempData["ProfileStatus"] = "Профилът е обновен.";
            return RedirectToAction(nameof(Profile), new { tab = "details" });
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveAddress([Bind(Prefix = "Address")] AddressFormViewModel address)
        {
            var user = await GetCurrentUserAsync();
            if (user == null)
                return RedirectToAction(nameof(Login));

            if (!ModelState.IsValid)
                return View("Profile", await BuildProfileViewModelAsync(user, address: address, activeTab: "addresses", showAddressModal: true));

            UserAddress entity;
            if (address.N.HasValue)
            {
                var existing = await _identityDb.UsersAddresses.FirstOrDefaultAsync(a => a.N == address.N.Value && a.UserN == user.N);
                if (existing == null)
                    return NotFound();
                entity = existing;
                entity.UpdatedAt = DateTime.Now;
            }
            else
            {
                entity = new UserAddress
                {
                    UserN = user.N,
                    CreatedAt = DateTime.Now
                };
                _identityDb.UsersAddresses.Add(entity);
            }

            entity.FirstName = address.FirstName.Trim();
            entity.LastName = address.LastName.Trim();
            entity.PhoneNumber = NormalizeOptional(address.PhoneNumber);
            entity.Email = address.Email.Trim();
            entity.AddressLine1 = address.AddressLine1.Trim();
            entity.AddressLine2 = NormalizeOptional(address.AddressLine2);
            entity.City = address.City.Trim();
            entity.PostalCode = NormalizeOptional(address.PostalCode);
            entity.Country = address.Country.Trim();

            var hasOtherAddresses = await _identityDb.UsersAddresses
                .AnyAsync(a => a.UserN == user.N && (!address.N.HasValue || a.N != address.N.Value));
            entity.IsDefault = address.IsDefault || !hasOtherAddresses;
            if (entity.IsDefault)
            {
                var otherDefaults = await _identityDb.UsersAddresses
                    .Where(a => a.UserN == user.N && (!address.N.HasValue || a.N != address.N.Value))
                    .ToListAsync();
                foreach (var other in otherDefaults)
                    other.IsDefault = false;
            }

            await _identityDb.SaveChangesAsync();
            TempData["ProfileStatus"] = address.N.HasValue ? "Адресът е обновен." : "Адресът е добавен.";
            return RedirectToAction(nameof(Profile), new { tab = "addresses" });
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAddress(int n)
        {
            var user = await GetCurrentUserAsync();
            if (user == null)
                return RedirectToAction(nameof(Login));

            var address = await _identityDb.UsersAddresses.FirstOrDefaultAsync(a => a.N == n && a.UserN == user.N);
            if (address == null)
                return NotFound();

            var wasDefault = address.IsDefault;
            _identityDb.UsersAddresses.Remove(address);
            await _identityDb.SaveChangesAsync();

            if (wasDefault)
            {
                var next = await _identityDb.UsersAddresses
                    .Where(a => a.UserN == user.N)
                    .OrderByDescending(a => a.UpdatedAt ?? a.CreatedAt)
                    .FirstOrDefaultAsync();
                if (next != null)
                {
                    next.IsDefault = true;
                    await _identityDb.SaveChangesAsync();
                }
            }

            TempData["ProfileStatus"] = "Адресът е изтрит.";
            return RedirectToAction(nameof(Profile), new { tab = "addresses" });
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword([Bind(Prefix = "Password")] ChangePasswordViewModel password)
        {
            var user = await GetCurrentUserAsync();
            if (user == null)
                return RedirectToAction(nameof(Login));

            if (!ModelState.IsValid)
                return View("Profile", await BuildProfileViewModelAsync(user, password: password, activeTab: "password"));

            if (!await _userService.VerifyPasswordAsync(user.UserName, password.CurrentPassword))
            {
                ModelState.AddModelError("Password.CurrentPassword", "Текущата парола е грешна.");
                return View("Profile", await BuildProfileViewModelAsync(user, password: password, activeTab: "password"));
            }

            await _userService.ChangePasswordAsync(user.UserName, password.NewPassword);
            user = await GetCurrentUserAsync();
            if (user != null)
                await _userService.SignInAsync(HttpContext, user, rememberMe: false);

            TempData["ProfileStatus"] = "Паролата е сменена успешно.";
            return RedirectToAction(nameof(Profile), new { tab = "password" });
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> WithdrawMarketingConsent()
        {
            var user = await GetCurrentUserAsync();
            if (user == null)
                return RedirectToAction(nameof(Login));

            user.MarketingConsent = false;
            _identityDb.UserConsents.Add(BuildMarketingConsent(user.N, granted: false));

            await _identityDb.SaveChangesAsync();
            TempData["ProfileStatus"] = "Маркетинг съгласието е оттеглено.";
            return RedirectToAction(nameof(Profile), new { tab = "privacy" });
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAccount([Bind(Prefix = "DeleteAccount")] DeleteAccountViewModel deleteAccount)
        {
            var user = await GetCurrentUserAsync();
            if (user == null)
                return RedirectToAction(nameof(Login));

            if (!ModelState.IsValid || !deleteAccount.ConfirmDeletion)
            {
                if (!deleteAccount.ConfirmDeletion)
                    ModelState.AddModelError("DeleteAccount.ConfirmDeletion", "Потвърдете, че желаете изтриване на акаунта.");
                return View("Profile", await BuildProfileViewModelAsync(user, deleteAccount: deleteAccount, activeTab: "privacy"));
            }

            if (!await _userService.VerifyPasswordAsync(user.UserName, deleteAccount.Password))
            {
                ModelState.AddModelError("DeleteAccount.Password", "Паролата е грешна.");
                return View("Profile", await BuildProfileViewModelAsync(user, deleteAccount: deleteAccount, activeTab: "privacy"));
            }

            var marker = Guid.NewGuid().ToString("N");
            user.IsActive = false;
            user.FirstName = null;
            user.LastName = null;
            user.FullName = "Изтрит потребител";
            user.DisplayName = "Изтрит потребител";
            user.PhoneNumber = null;
            user.AddressLine1 = null;
            user.AddressLine2 = null;
            user.City = null;
            user.PostalCode = null;
            user.Country = null;
            user.MarketingConsent = false;
            user.Email = $"deleted-{user.N}-{marker}@deleted.local";
            user.UserName = user.Email;
            user.SecurityStamp = Guid.NewGuid().ToString();

            var addresses = await _identityDb.UsersAddresses.Where(a => a.UserN == user.N).ToListAsync();
            _identityDb.UsersAddresses.RemoveRange(addresses);
            _identityDb.UserConsents.Add(BuildMarketingConsent(user.N, granted: false));

            await _identityDb.SaveChangesAsync();
            await _userService.ForceSignOutAllSessionsAsync(user.Id);
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Index", "Home");
        }

        [Authorize]
        public IActionResult ClaimsInfo()
        {
            var claims = User.Claims.Select(c => new { c.Type, c.Value }).ToList();
            return Json(claims);
        }

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

        private async Task<ApplicationUser?> GetCurrentUserAsync()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId))
                return null;

            return await _identityDb.Users.FirstOrDefaultAsync(u => u.Id == userId && u.IsActive);
        }

        private async Task<ProfileViewModel> BuildProfileViewModelAsync(
            ApplicationUser user,
            ProfileDetailsViewModel? details = null,
            ChangePasswordViewModel? password = null,
            AddressFormViewModel? address = null,
            DeleteAccountViewModel? deleteAccount = null,
            string? activeTab = null,
            bool showAddressModal = false)
        {
            var orders = (await _orderRepository.GetByUserNAsync(user.N))
                .Select(ToProfileOrderRow)
                .ToList();
            var (firstName, lastName) = ResolveProfileNames(user);
            var addresses = await _identityDb.UsersAddresses
                .Where(a => a.UserN == user.N)
                .OrderByDescending(a => a.IsDefault)
                .ThenByDescending(a => a.UpdatedAt ?? a.CreatedAt)
                .Select(a => new ProfileAddressViewModel
                {
                    N = a.N,
                    FirstName = a.FirstName,
                    LastName = a.LastName,
                    PhoneNumber = a.PhoneNumber,
                    Email = a.Email,
                    AddressLine1 = a.AddressLine1,
                    AddressLine2 = a.AddressLine2,
                    City = a.City,
                    PostalCode = a.PostalCode,
                    Country = a.Country,
                    IsDefault = a.IsDefault
                })
                .ToListAsync();

            return new ProfileViewModel
            {
                Details = details ?? new ProfileDetailsViewModel
                {
                    FirstName = firstName,
                    LastName = lastName,
                    PhoneNumber = user.PhoneNumber,
                    Email = user.Email
                },
                Password = password ?? new ChangePasswordViewModel(),
                Address = address ?? BuildDefaultAddressForm(user, firstName, lastName),
                DeleteAccount = deleteAccount ?? new DeleteAccountViewModel(),
                MarketingConsent = user.MarketingConsent,
                ActiveTab = NormalizeProfileTab(activeTab),
                ShowAddressModal = showAddressModal,
                Addresses = addresses,
                CurrentOrders = orders
                    .Where(o => o.Status is OrderStatus.Pending or OrderStatus.Confirmed or OrderStatus.Shipped)
                    .ToList(),
                OrderHistory = orders
                    .Where(o => o.Status is OrderStatus.Delivered or OrderStatus.Cancelled)
                    .ToList(),
                StatusMessage = TempData["ProfileStatus"] as string,
                ErrorMessage = TempData["ProfileError"] as string
            };
        }

        private UserConsent BuildMarketingConsent(int userN, bool granted) => new()
        {
            UserN = userN,
            ConsentType = ConsentTypes.Marketing,
            Granted = granted,
            PolicyVersion = "1.0",
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            UserAgent = Request.Headers.UserAgent.ToString(),
            ConsentedAt = DateTime.UtcNow
        };

        private static (string FirstName, string LastName) ResolveProfileNames(ApplicationUser user)
        {
            var firstName = user.FirstName?.Trim() ?? "";
            var lastName = user.LastName?.Trim() ?? "";

            if (!string.IsNullOrWhiteSpace(firstName) || !string.IsNullOrWhiteSpace(lastName))
                return (firstName, lastName);

            var fullName = user.FullName?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(fullName) ||
                string.Equals(fullName, user.Email, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fullName, user.UserName, StringComparison.OrdinalIgnoreCase))
            {
                return ("", "");
            }

            var parts = fullName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return parts.Length switch
            {
                0 => ("", ""),
                1 => (parts[0], ""),
                _ => (parts[0], parts[1])
            };
        }

        private static ProfileOrderRowViewModel ToProfileOrderRow(Order order) => new()
        {
            N = order.N,
            Status = order.Status,
            TotalAmount = order.TotalAmount,
            CreatedAt = order.CreatedAt,
            UpdatedAt = order.UpdatedAt,
            ItemCount = order.OrderItems.Sum(i => i.Quantity),
            StatusLabel = StatusLabel(order.Status),
            StatusCssClass = StatusCssClass(order.Status)
        };

        private static string StatusLabel(OrderStatus status) => status switch
        {
            OrderStatus.Pending => "Чакаща",
            OrderStatus.Confirmed => "Потвърдена",
            OrderStatus.Shipped => "Изпратена",
            OrderStatus.Delivered => "Доставена",
            OrderStatus.Cancelled => "Отказана",
            _ => status.ToString()
        };

        private static string StatusCssClass(OrderStatus status) => status switch
        {
            OrderStatus.Pending => "bg-warning text-dark",
            OrderStatus.Confirmed => "bg-primary",
            OrderStatus.Shipped => "bg-info text-dark",
            OrderStatus.Delivered => "bg-success",
            OrderStatus.Cancelled => "bg-secondary",
            _ => "bg-light text-dark"
        };

        private static string? NormalizeOptional(string? value)
            => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static AddressFormViewModel BuildDefaultAddressForm(ApplicationUser user, string firstName, string lastName) => new()
        {
            FirstName = firstName,
            LastName = lastName,
            PhoneNumber = user.PhoneNumber,
            Email = user.Email,
            AddressLine1 = string.Empty,
            City = string.Empty,
            Country = "България"
        };

        private static string NormalizeProfileTab(string? tab) => tab switch
        {
            "history" => "history",
            "addresses" => "addresses",
            "details" => "details",
            "password" => "password",
            "privacy" => "privacy",
            _ => "current"
        };
    }
}
