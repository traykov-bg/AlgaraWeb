using Algara.Data.Models;
using Algara.Data.Repositories;
using Algara.Identity.Data;
using Algara.Identity.Models;
using Algara.Identity.Services;
using Algara.Identity.Authorization;
using Algara.Web.ViewModels;
using Algara.Web.Services.Email;
using Algara.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;

namespace Algara.Web.Controllers
{
    public class AccountController : Controller
    {
        private readonly IUserService _userService;
        private readonly IdentityDbContext _identityDb;
        private readonly IOrderRepository _orderRepository;
        private readonly IAccountEmailService _accountEmail;
        private readonly ICustomerManagementService _customerManagement;

        public AccountController(
            IUserService userService,
            IdentityDbContext identityDb,
            IOrderRepository orderRepository,
            IAccountEmailService accountEmail,
            ICustomerManagementService customerManagement)
        {
            _userService = userService;
            _identityDb = identityDb;
            _orderRepository = orderRepository;
            _accountEmail = accountEmail;
            _customerManagement = customerManagement;
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
        [EnableRateLimiting("AccountEmail")]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            if (!ModelState.IsValid)
            {
                ClearRegistrationPasswords(model);
                return View(model);
            }

            var data = new RegistrationData
            {
                Email = model.Email,
                Password = model.Password,
                FirstName = model.FirstName.Trim(),
                LastName = model.LastName.Trim(),
                PhoneNumber = model.PhoneNumber,
                MarketingConsent = model.MarketingConsent,
                TermsAccepted = model.TermsAccepted,
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                UserAgent = Request.Headers.UserAgent.ToString()
            };

            var result = await _accountEmail.RegisterAsync(data, HttpContext.RequestAborted);
            ClearRegistrationPasswords(model);
            if (!result.Success)
            {
                ModelState.AddModelError(string.Empty, result.Error ?? "Регистрацията не е завършена. Опитайте отново.");
                return View(model);
            }

            return RedirectToAction(nameof(AccountEmailController.RegistrationPending), "AccountEmail");
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

            if (user.IsActive && user.RequireEmailConfirmation && !user.EmailConfirmed &&
                await _userService.VerifyPasswordAsync(model.Email, model.Password))
            {
                ModelState.AddModelError(string.Empty, "Потвърдете имейла си преди първия вход. Можете да поискате ново писмо за потвърждение.");
                return LoginFailure(model, returnUrl, isAjax);
            }

            if (!await _userService.ValidateUserAsync(model.Email, model.Password))
            {
                ModelState.AddModelError(string.Empty, "Грешен имейл или парола.");
                return LoginFailure(model, returnUrl, isAjax);
            }

            try
            {
                await _userService.SignInAsync(HttpContext, user, model.RememberMe, model.TimeZoneOffset);
            }
            catch (InvalidOperationException)
            {
                ModelState.AddModelError(string.Empty, "Достъпът е променен. Опитайте да влезете отново.");
                return LoginFailure(model, returnUrl, isAjax);
            }

            if (isAjax)
            {
                var target = !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)
                    ? returnUrl
                    : Url.Action("Index", "Home");
                return Json(new { success = true, redirectUrl = target });
            }

            return RedirectToLocal(returnUrl);
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

            var result = await _customerManagement.EditOwnProfileAsync(User, new CustomerProfileUpdate
            {
                ExpectedAccountVersion = details.ExpectedVersion,
                FirstName = details.FirstName,
                LastName = details.LastName,
                PhoneNumber = details.PhoneNumber
            }, HttpContext.RequestAborted);
            if (!result.Success)
            {
                ModelState.AddModelError(string.Empty, result.Error ?? "Профилът не е обновен. Презаредете страницата и опитайте отново.");
                return View("Profile", await BuildProfileViewModelAsync(user, details: details, activeTab: "details"));
            }
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

            var result = await _customerManagement.SaveOwnAddressAsync(User, new CustomerAddressUpdate
            {
                N = address.N,
                ExpectedAccountVersion = address.ExpectedAccountVersion,
                ExpectedAddressesVersion = address.ExpectedVersion,
                FirstName = address.FirstName,
                LastName = address.LastName,
                PhoneNumber = address.PhoneNumber,
                Email = address.Email,
                AddressLine1 = address.AddressLine1,
                AddressLine2 = address.AddressLine2,
                City = address.City,
                PostalCode = address.PostalCode,
                Country = address.Country,
                IsDefault = address.IsDefault
            }, HttpContext.RequestAborted);
            if (!result.Success)
            {
                ModelState.AddModelError(string.Empty, result.Error ?? "Адресът не е записан. Презаредете страницата и опитайте отново.");
                return View("Profile", await BuildProfileViewModelAsync(user, address: address, activeTab: "addresses", showAddressModal: true));
            }
            TempData["ProfileStatus"] = address.N.HasValue ? "Адресът е обновен." : "Адресът е добавен.";
            return RedirectToAction(nameof(Profile), new { tab = "addresses" });
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAddress(int n, string? expectedVersion, string? expectedAccountVersion)
        {
            var user = await GetCurrentUserAsync();
            if (user == null)
                return RedirectToAction(nameof(Login));

            var result = await _customerManagement.DeleteOwnAddressAsync(User, new CustomerAddressAction
            {
                AddressN = n,
                ExpectedAccountVersion = expectedAccountVersion,
                ExpectedAddressesVersion = expectedVersion
            }, HttpContext.RequestAborted);
            if (!result.Success)
            {
                TempData["ProfileError"] = result.Error ?? "Адресът не е изтрит. Презаредете страницата и опитайте отново.";
                return RedirectToAction(nameof(Profile), new { tab = "addresses" });
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

            if (!await _userService.ChangePasswordAsync(user.UserName, password.NewPassword, User))
            {
                ModelState.AddModelError("Password.CurrentPassword", "Акаунтът е променен. Презаредете страницата и влезте отново.");
                return View("Profile", await BuildProfileViewModelAsync(user, activeTab: "password"));
            }
            user = await GetCurrentUserAsync();
            if (user != null)
            {
                try { await _userService.SignInAsync(HttpContext, user, rememberMe: false); }
                catch (InvalidOperationException) { return RedirectToAction(nameof(Login)); }
            }

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

            var access = HttpContext.RequestServices.GetRequiredService<AccessManagementService>();
            var result = await access.DeleteOwnAccountAsync(User, user.SecurityStamp,
                HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString(), HttpContext.RequestAborted);
            if (!result.Succeeded)
            {
                ModelState.AddModelError("DeleteAccount.ConfirmDeletion", result.Error ?? "Акаунтът не е изтрит.");
                return View("Profile", await BuildProfileViewModelAsync(user, deleteAccount: deleteAccount, activeTab: "privacy"));
            }
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

        private void ClearRegistrationPasswords(RegisterViewModel model)
        {
            model.Password = model.ConfirmPassword = string.Empty;
            foreach (var key in new[] { nameof(model.Password), nameof(model.ConfirmPassword) })
                if (ModelState.TryGetValue(key, out var entry)) entry.RawValue = entry.AttemptedValue = null;
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
            var addressEntities = await _identityDb.UsersAddresses.AsNoTracking()
                .Where(a => a.UserN == user.N)
                .OrderByDescending(a => a.IsDefault)
                .ThenByDescending(a => a.UpdatedAt ?? a.CreatedAt)
                .ToListAsync();
            var accountVersion = CustomerManagementVersions.Account(user);
            var addressesVersion = CustomerManagementVersions.AddressBook(addressEntities);
            var addresses = addressEntities.Select(a => new ProfileAddressViewModel
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
                .ToList();

            if (address == null)
            {
                address = BuildDefaultAddressForm(user, firstName, lastName);
                address.ExpectedVersion = addressesVersion;
                address.ExpectedAccountVersion = accountVersion;
            }

            return new ProfileViewModel
            {
                AccountVersion = accountVersion,
                AddressBookVersion = addressesVersion,
                Details = details ?? new ProfileDetailsViewModel
                {
                    ExpectedVersion = accountVersion,
                    FirstName = firstName,
                    LastName = lastName,
                    PhoneNumber = user.PhoneNumber,
                    Email = user.Email
                },
                Password = password ?? new ChangePasswordViewModel(),
                Address = address,
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
