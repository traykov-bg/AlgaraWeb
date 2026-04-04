using Algara.Data.Data;
using Algara.Data.Models;
using Algara.Data.Repositories;
using Algara.Identity.Data;
using Algara.Web.Helpers;
using Algara.Web.ViewModels.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Algara.Web.Controllers
{
    [Authorize(Roles = "Admin")]
    [Route("admin")]
    public class AdminController : Controller
    {
        private readonly ShopDbContext      _shopDb;
        private readonly IdentityDbContext  _identityDb;
        private readonly IProductRepository  _productRepo;
        private readonly ICategoryRepository _categoryRepo;
        private readonly IOrderRepository    _orderRepo;
        private readonly IUserService        _userService;
        private readonly ILogger<AdminController> _logger;

        public AdminController(
            ShopDbContext shopDb,
            IdentityDbContext identityDb,
            IProductRepository productRepo,
            ICategoryRepository categoryRepo,
            IOrderRepository orderRepo,
            IUserService userService,
            ILogger<AdminController> logger)
        {
            _shopDb       = shopDb;
            _identityDb   = identityDb;
            _productRepo  = productRepo;
            _categoryRepo = categoryRepo;
            _orderRepo    = orderRepo;
            _userService  = userService;
            _logger       = logger;
        }

        // ═══════════════════════════════════════════════════════════
        //  DASHBOARD
        // ═══════════════════════════════════════════════════════════

        [HttpGet("")]
        [HttpGet("index")]
        public async Task<IActionResult> Index()
        {
            var vm = new AdminDashboardViewModel
            {
                TotalActiveProducts = await _shopDb.Products.CountAsync(p => p.IsActive),
                TotalCategories     = await _shopDb.Categories.CountAsync(c => c.IsActive),
                TotalUsers          = await _identityDb.Users.CountAsync(),
                OrdersPending       = await _orderRepo.GetCountByStatusAsync(OrderStatus.Pending),
                OrdersConfirmed     = await _orderRepo.GetCountByStatusAsync(OrderStatus.Confirmed),
                OrdersShipped       = await _orderRepo.GetCountByStatusAsync(OrderStatus.Shipped),
                OrdersDelivered     = await _orderRepo.GetCountByStatusAsync(OrderStatus.Delivered),
                OrdersCancelled     = await _orderRepo.GetCountByStatusAsync(OrderStatus.Cancelled),
            };
            return View(vm);
        }

        // ═══════════════════════════════════════════════════════════
        //  PRODUCTS
        // ═══════════════════════════════════════════════════════════

        [HttpGet("products")]
        public async Task<IActionResult> Products(int page = 1)
        {
            const int pageSize = 20;
            var query = _shopDb.Products.Include(p => p.Category).OrderBy(p => p.Name);

            var total = await query.CountAsync();
            var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            var vm = new AdminProductListViewModel
            {
                Products    = items,
                CurrentPage = page,
                TotalPages  = (int)Math.Ceiling(total / (double)pageSize),
                PageSize    = pageSize,
            };
            return View(vm);
        }

        [HttpGet("products/create")]
        public async Task<IActionResult> ProductCreate()
        {
            var vm = new AdminProductFormViewModel
            {
                Categories = await _shopDb.Categories.Where(c => c.IsActive).OrderBy(c => c.Name).ToListAsync(),
            };
            return View(vm);
        }

        [HttpPost("products/create")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ProductCreate(AdminProductFormViewModel vm)
        {
            if (!ModelState.IsValid)
            {
                vm.Categories = await _shopDb.Categories.Where(c => c.IsActive).OrderBy(c => c.Name).ToListAsync();
                return View(vm);
            }

            var product = new Product
            {
                Name           = vm.Name,
                Description    = vm.Description,
                Price          = vm.Price,
                ImageUrl       = vm.ImageUrl,
                IsCustomizable = vm.IsCustomizable,
                IsFeatured     = vm.IsFeatured,
                IsActive       = vm.IsActive,
                CategoryN      = vm.CategoryN,
            };
            await _productRepo.AddAsync(product);

            TempData["Success"] = $"Продуктът \"{product.Name}\" е добавен успешно.";
            return RedirectToAction(nameof(Products));
        }

        [HttpGet("products/edit/{n}")]
        public async Task<IActionResult> ProductEdit(int n)
        {
            var product = await _shopDb.Products.FindAsync(n);
            if (product == null) return NotFound();

            var vm = new AdminProductFormViewModel
            {
                N              = product.N,
                Name           = product.Name,
                Description    = product.Description,
                Price          = product.Price,
                ImageUrl       = product.ImageUrl,
                IsCustomizable = product.IsCustomizable,
                IsFeatured     = product.IsFeatured,
                IsActive       = product.IsActive,
                CategoryN      = product.CategoryN,
                Categories     = await _shopDb.Categories.Where(c => c.IsActive).OrderBy(c => c.Name).ToListAsync(),
            };
            return View(vm);
        }

        [HttpPost("products/edit/{n}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ProductEdit(AdminProductFormViewModel vm)
        {
            if (!ModelState.IsValid)
            {
                vm.Categories = await _shopDb.Categories.Where(c => c.IsActive).OrderBy(c => c.Name).ToListAsync();
                return View(vm);
            }

            var product = await _shopDb.Products.FindAsync(vm.N);
            if (product == null) return NotFound();

            product.Name           = vm.Name;
            product.Description    = vm.Description;
            product.Price          = vm.Price;
            product.ImageUrl       = vm.ImageUrl;
            product.IsCustomizable = vm.IsCustomizable;
            product.IsFeatured     = vm.IsFeatured;
            product.IsActive       = vm.IsActive;
            product.CategoryN      = vm.CategoryN;

            await _productRepo.UpdateAsync(product);

            TempData["Success"] = $"Продуктът \"{product.Name}\" е обновен успешно.";
            return RedirectToAction(nameof(Products));
        }

        [HttpPost("products/toggle/{n}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ProductToggle(int n)
        {
            var product = await _shopDb.Products.FindAsync(n);
            if (product == null) return NotFound();

            product.IsActive = !product.IsActive;
            await _shopDb.SaveChangesAsync();

            TempData["Success"] = product.IsActive
                ? $"\"{product.Name}\" е активиран."
                : $"\"{product.Name}\" е деактивиран.";
            return RedirectToAction(nameof(Products));
        }

        // ═══════════════════════════════════════════════════════════
        //  CATEGORIES
        // ═══════════════════════════════════════════════════════════

        [HttpGet("categories")]
        public async Task<IActionResult> Categories()
        {
            var categories = await _shopDb.Categories
                .Include(c => c.Products)
                .OrderBy(c => c.Name)
                .ToListAsync();
            return View(categories);
        }

        [HttpGet("categories/create")]
        public IActionResult CategoryCreate()
            => View(new AdminCategoryFormViewModel());

        [HttpPost("categories/create")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CategoryCreate(AdminCategoryFormViewModel vm)
        {
            // Ако slug-ът е празен, го генерираме от Name
            if (string.IsNullOrWhiteSpace(vm.Slug))
                vm.Slug = SlugHelper.Generate(vm.Name);

            if (!ModelState.IsValid) return View(vm);

            var category = new Category
            {
                Name        = vm.Name,
                Slug        = vm.Slug,
                Description = vm.Description,
                IsFeatured  = vm.IsFeatured,
                IsActive    = vm.IsActive,
            };
            await _categoryRepo.AddAsync(category);

            TempData["Success"] = $"Категорията \"{category.Name}\" е добавена успешно.";
            return RedirectToAction(nameof(Categories));
        }

        [HttpGet("categories/edit/{n}")]
        public async Task<IActionResult> CategoryEdit(int n)
        {
            var category = await _shopDb.Categories.FindAsync(n);
            if (category == null) return NotFound();

            var vm = new AdminCategoryFormViewModel
            {
                N           = category.N,
                Name        = category.Name,
                Slug        = category.Slug,
                Description = category.Description,
                IsFeatured  = category.IsFeatured,
                IsActive    = category.IsActive,
            };
            return View(vm);
        }

        [HttpPost("categories/edit/{n}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CategoryEdit(AdminCategoryFormViewModel vm)
        {
            // Ако slug-ът е празен, го генерираме от Name
            if (string.IsNullOrWhiteSpace(vm.Slug))
                vm.Slug = SlugHelper.Generate(vm.Name);

            if (!ModelState.IsValid) return View(vm);

            var category = await _shopDb.Categories.FindAsync(vm.N);
            if (category == null) return NotFound();

            category.Name        = vm.Name;
            category.Slug        = vm.Slug;
            category.Description = vm.Description;
            category.IsFeatured  = vm.IsFeatured;
            category.IsActive    = vm.IsActive;

            await _categoryRepo.UpdateAsync(category);

            TempData["Success"] = $"Категорията \"{category.Name}\" е обновена успешно.";
            return RedirectToAction(nameof(Categories));
        }

        [HttpPost("categories/toggle/{n}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CategoryToggle(int n)
        {
            var category = await _shopDb.Categories.FindAsync(n);
            if (category == null) return NotFound();

            category.IsActive = !category.IsActive;
            await _shopDb.SaveChangesAsync();

            TempData["Success"] = category.IsActive
                ? $"\"{category.Name}\" е активирана."
                : $"\"{category.Name}\" е деактивирана.";
            return RedirectToAction(nameof(Categories));
        }

        // ═══════════════════════════════════════════════════════════
        //  ORDERS
        // ═══════════════════════════════════════════════════════════

        [HttpGet("orders")]
        public async Task<IActionResult> Orders(OrderStatus? status = null)
        {
            var allOrders = await _orderRepo.GetAllAsync();
            var orders    = status.HasValue
                ? allOrders.Where(o => o.Status == status)
                : allOrders;

            var userNs  = orders.Select(o => o.UserN).Distinct().ToList();
            var userMap = await _identityDb.Users
                .Where(u => userNs.Contains(u.N))
                .ToDictionaryAsync(u => u.N);

            var rows = orders.Select(o => new AdminOrderRowViewModel
            {
                Order           = o,
                UserDisplayName = userMap.TryGetValue(o.UserN, out var u)  ? (u.DisplayName ?? u.UserName) : $"#{o.UserN}",
                UserEmail       = userMap.TryGetValue(o.UserN, out var u2) ? u2.Email                       : string.Empty,
            });

            return View(new AdminOrderListViewModel { Rows = rows, StatusFilter = status });
        }

        [HttpGet("orders/{n}")]
        public async Task<IActionResult> OrderDetail(int n)
        {
            var order = await _orderRepo.GetByNAsync(n);
            if (order == null) return NotFound();

            var user = await _identityDb.Users.FirstOrDefaultAsync(u => u.N == order.UserN);

            var vm = new AdminOrderDetailViewModel
            {
                Order           = order,
                UserDisplayName = user != null ? (user.DisplayName ?? user.UserName) : $"#{order.UserN}",
                UserEmail       = user?.Email ?? string.Empty,
            };
            return View(vm);
        }

        [HttpPost("orders/{n}/status")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> OrderUpdateStatus(int n, OrderStatus status)
        {
            await _orderRepo.UpdateStatusAsync(n, status);
            TempData["Success"] = "Статусът на поръчката е обновен.";
            return RedirectToAction(nameof(OrderDetail), new { n });
        }

        // ═══════════════════════════════════════════════════════════
        //  USERS
        // ═══════════════════════════════════════════════════════════

        [HttpGet("users")]
        public async Task<IActionResult> Users()
        {
            var users = await _identityDb.Users.OrderBy(u => u.UserName).ToListAsync();

            var rows = new List<AdminUserRowViewModel>();
            foreach (var user in users)
            {
                var roles = await _userService.GetRolesAsync(user);
                rows.Add(new AdminUserRowViewModel { User = user, Roles = roles.ToList() });
            }

            ViewBag.AllRoles = (await _identityDb.Roles.OrderBy(r => r.Name).ToListAsync())
                .Select(r => new SelectListItem(r.Name, r.Name))
                .ToList();

            return View(new AdminUserListViewModel { Rows = rows });
        }

        [HttpPost("users/assign-role")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AssignRole(AdminUserRoleViewModel vm)
        {
            var result = await _userService.AddUserToRoleAsync(vm.Username, vm.RoleName);
            TempData[result ? "Success" : "Error"] = result
                ? $"Ролята \"{vm.RoleName}\" е добавена на {vm.Username}."
                : $"Неуспешно добавяне на роля (потребителят може вече да я има).";
            return RedirectToAction(nameof(Users));
        }

        [HttpPost("users/remove-role")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveRole(AdminUserRoleViewModel vm)
        {
            var result = await _userService.RemoveUserFromRoleAsync(vm.Username, vm.RoleName);
            TempData[result ? "Success" : "Error"] = result
                ? $"Ролята \"{vm.RoleName}\" е премахната от {vm.Username}."
                : $"Неуспешно премахване на роля.";
            return RedirectToAction(nameof(Users));
        }
    }
}
