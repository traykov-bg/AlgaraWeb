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
using Microsoft.AspNetCore.Hosting;

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
        private readonly IHeroSlideRepository _heroSlideRepo;
        private readonly IPromotionRepository _promotionRepo;
        private readonly IUserService        _userService;
        private readonly ILogger<AdminController> _logger;
        private readonly IWebHostEnvironment _env;

        public AdminController(
            ShopDbContext shopDb,
            IdentityDbContext identityDb,
            IProductRepository productRepo,
            ICategoryRepository categoryRepo,
            IOrderRepository orderRepo,
            IHeroSlideRepository heroSlideRepo,
            IPromotionRepository promotionRepo,
            IUserService userService,
            ILogger<AdminController> logger,
            IWebHostEnvironment env)
        {
            _shopDb        = shopDb;
            _identityDb    = identityDb;
            _productRepo   = productRepo;
            _categoryRepo  = categoryRepo;
            _orderRepo     = orderRepo;
            _heroSlideRepo = heroSlideRepo;
            _promotionRepo = promotionRepo;
            _userService   = userService;
            _logger        = logger;
            _env           = env;
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
        public async Task<IActionResult> Products(int page = 1, string sort = "name_asc", string cats = "")
        {
            const int pageSize = 20;

            var allCategories = await _shopDb.Categories.OrderBy(c => c.Name).ToListAsync();

            IQueryable<Product> baseQuery = _shopDb.Products.Include(p => p.Category);

            // Apply category filter
            if (!string.IsNullOrWhiteSpace(cats))
            {
                var catIds = cats.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => int.TryParse(s.Trim(), out var n) ? n : -1)
                    .Where(n => n >= 0)
                    .ToHashSet();

                if (catIds.Count > 0)
                {
                    bool hasNull  = catIds.Contains(0);
                    var  realIds  = catIds.Where(n => n > 0).ToHashSet();

                    if (hasNull && realIds.Count > 0)
                        baseQuery = baseQuery.Where(p => p.CategoryN == null || realIds.Contains(p.CategoryN!.Value));
                    else if (hasNull)
                        baseQuery = baseQuery.Where(p => p.CategoryN == null);
                    else
                        baseQuery = baseQuery.Where(p => p.CategoryN != null && realIds.Contains(p.CategoryN!.Value));
                }
            }

            // Apply multi-column sort
            IOrderedQueryable<Product>? ordered = null;
            foreach (var part in sort.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()))
            {
                var segments = part.Split('_');
                var col      = segments[0];
                var asc      = segments.Length < 2 || segments[1] != "desc";

                if (ordered == null)
                    ordered = (col, asc) switch
                    {
                        ("n",        true)  => baseQuery.OrderBy(p => p.N),
                        ("n",        false) => baseQuery.OrderByDescending(p => p.N),
                        ("name",     true)  => baseQuery.OrderBy(p => p.Name),
                        ("name",     false) => baseQuery.OrderByDescending(p => p.Name),
                        ("category", true)  => baseQuery.OrderBy(p => p.Category!.Name),
                        ("category", false) => baseQuery.OrderByDescending(p => p.Category!.Name),
                        ("price",    true)  => baseQuery.OrderBy(p => p.Price),
                        ("price",    false) => baseQuery.OrderByDescending(p => p.Price),
                        ("featured", true)  => baseQuery.OrderBy(p => p.IsFeatured),
                        ("featured", false) => baseQuery.OrderByDescending(p => p.IsFeatured),
                        ("status",   true)  => baseQuery.OrderBy(p => p.IsActive),
                        ("status",   false) => baseQuery.OrderByDescending(p => p.IsActive),
                        _                   => baseQuery.OrderBy(p => p.Name),
                    };
                else
                    ordered = (col, asc) switch
                    {
                        ("n",        true)  => ordered.ThenBy(p => p.N),
                        ("n",        false) => ordered.ThenByDescending(p => p.N),
                        ("name",     true)  => ordered.ThenBy(p => p.Name),
                        ("name",     false) => ordered.ThenByDescending(p => p.Name),
                        ("category", true)  => ordered.ThenBy(p => p.Category!.Name),
                        ("category", false) => ordered.ThenByDescending(p => p.Category!.Name),
                        ("price",    true)  => ordered.ThenBy(p => p.Price),
                        ("price",    false) => ordered.ThenByDescending(p => p.Price),
                        ("featured", true)  => ordered.ThenBy(p => p.IsFeatured),
                        ("featured", false) => ordered.ThenByDescending(p => p.IsFeatured),
                        ("status",   true)  => ordered.ThenBy(p => p.IsActive),
                        ("status",   false) => ordered.ThenByDescending(p => p.IsActive),
                        _                   => ordered.ThenBy(p => p.Name),
                    };
            }

            ordered ??= baseQuery.OrderBy(p => p.Name);

            var total = await baseQuery.CountAsync();
            var items = await ordered.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            var vm = new AdminProductListViewModel
            {
                Products       = items,
                CurrentPage    = page,
                TotalPages     = (int)Math.Ceiling(total / (double)pageSize),
                PageSize       = pageSize,
                Sort           = sort,
                CategoryFilter = cats,
                AllCategories  = allCategories,
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
                if (vm.CategoryN.HasValue)
                    vm.AvailableSubCategories = await _shopDb.SubCategories
                        .Where(sc => sc.CategoryN == vm.CategoryN && sc.IsActive)
                        .OrderBy(sc => sc.Name).ToListAsync();
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
            _shopDb.Products.Add(product);
            await _shopDb.SaveChangesAsync();

            foreach (var subN in vm.SelectedSubCategoryNs ?? [])
                _shopDb.ProductSubCategories.Add(new ProductSubCategory { ProductN = product.N, SubCategoryN = subN });
            await _shopDb.SaveChangesAsync();

            TempData["Success"] = $"Продуктът \"{product.Name}\" е добавен успешно.";
            return RedirectToAction(nameof(Products));
        }

        [HttpGet("products/edit/{n}")]
        public async Task<IActionResult> ProductEdit(int n)
        {
            var product = await _shopDb.Products
                .Include(p => p.ProductSubCategories)
                .FirstOrDefaultAsync(p => p.N == n);
            if (product == null) return NotFound();

            var vm = new AdminProductFormViewModel
            {
                N                     = product.N,
                Name                  = product.Name,
                Description           = product.Description,
                Price                 = product.Price,
                ImageUrl              = product.ImageUrl,
                IsCustomizable        = product.IsCustomizable,
                IsFeatured            = product.IsFeatured,
                IsActive              = product.IsActive,
                CategoryN             = product.CategoryN,
                Categories            = await _shopDb.Categories.Where(c => c.IsActive).OrderBy(c => c.Name).ToListAsync(),
                SelectedSubCategoryNs = product.ProductSubCategories.Select(psc => psc.SubCategoryN).ToList(),
            };
            if (product.CategoryN.HasValue)
                vm.AvailableSubCategories = await _shopDb.SubCategories
                    .Where(sc => sc.CategoryN == product.CategoryN && sc.IsActive)
                    .OrderBy(sc => sc.Name).ToListAsync();
            return View(vm);
        }

        [HttpPost("products/edit/{n}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ProductEdit(AdminProductFormViewModel vm)
        {
            if (!ModelState.IsValid)
            {
                vm.Categories = await _shopDb.Categories.Where(c => c.IsActive).OrderBy(c => c.Name).ToListAsync();
                if (vm.CategoryN.HasValue)
                    vm.AvailableSubCategories = await _shopDb.SubCategories
                        .Where(sc => sc.CategoryN == vm.CategoryN && sc.IsActive)
                        .OrderBy(sc => sc.Name).ToListAsync();
                return View(vm);
            }

            var product = await _shopDb.Products
                .Include(p => p.ProductSubCategories)
                .FirstOrDefaultAsync(p => p.N == vm.N);
            if (product == null) return NotFound();

            product.Name           = vm.Name;
            product.Description    = vm.Description;
            product.Price          = vm.Price;
            product.ImageUrl       = vm.ImageUrl;
            product.IsCustomizable = vm.IsCustomizable;
            product.IsFeatured     = vm.IsFeatured;
            product.IsActive       = vm.IsActive;
            product.CategoryN      = vm.CategoryN;

            // Обнови присвоените под-категории
            var selectedNs = (vm.SelectedSubCategoryNs ?? []).ToHashSet();
            var existingNs = product.ProductSubCategories.Select(psc => psc.SubCategoryN).ToHashSet();

            var toRemove = product.ProductSubCategories.Where(psc => !selectedNs.Contains(psc.SubCategoryN)).ToList();
            _shopDb.ProductSubCategories.RemoveRange(toRemove);

            foreach (var subN in selectedNs.Where(sn => !existingNs.Contains(sn)))
                _shopDb.ProductSubCategories.Add(new ProductSubCategory { ProductN = product.N, SubCategoryN = subN });

            await _shopDb.SaveChangesAsync();

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
            var category = await _shopDb.Categories
                .Include(c => c.SubCategories.Where(sc => sc.IsActive))
                .FirstOrDefaultAsync(c => c.N == n);
            if (category == null) return NotFound();

            var vm = new AdminCategoryFormViewModel
            {
                N             = category.N,
                Name          = category.Name,
                Slug          = category.Slug,
                Description   = category.Description,
                IsFeatured    = category.IsFeatured,
                IsActive      = category.IsActive,
                SubCategories = category.SubCategories
                    .Select(sc => new AdminSubCategoryViewModel { N = sc.N, Name = sc.Name, Slug = sc.Slug })
                    .ToList(),
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

            if (!ModelState.IsValid)
            {
                // Зареди под-категориите отново при грешка
                vm.SubCategories = await _shopDb.SubCategories
                    .Where(sc => sc.CategoryN == vm.N && sc.IsActive)
                    .Select(sc => new AdminSubCategoryViewModel { N = sc.N, Name = sc.Name, Slug = sc.Slug })
                    .ToListAsync();
                return View(vm);
            }

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

        [HttpPost("categories/{n}/subcategories/add")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubCategoryAdd(int n, string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                TempData["Error"] = "Наименованието на под-категорията е задължително.";
                return RedirectToAction(nameof(CategoryEdit), new { n });
            }

            var category = await _shopDb.Categories.FindAsync(n);
            if (category == null) return NotFound();

            var slug = SlugHelper.Generate(name);
            _shopDb.SubCategories.Add(new SubCategory
            {
                CategoryN = n,
                Name      = name.Trim(),
                Slug      = slug,
                IsActive  = true,
            });
            await _shopDb.SaveChangesAsync();

            TempData["Success"] = $"Под-категорията \"{name.Trim()}\" е добавена.";
            return RedirectToAction(nameof(CategoryEdit), new { n });
        }

        [HttpPost("categories/subcategories/delete/{sn}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubCategoryDelete(int sn)
        {
            var sub = await _shopDb.SubCategories.FindAsync(sn);
            if (sub == null) return NotFound();

            int categoryN = sub.CategoryN;
            sub.IsActive = false;
            await _shopDb.SaveChangesAsync();

            TempData["Success"] = $"Под-категорията \"{sub.Name}\" е премахната.";
            return RedirectToAction(nameof(CategoryEdit), new { n = categoryN });
        }

        // AJAX: връща под-категориите за дадена категория (използва се от формата за продукт)
        [HttpGet("categories/{n}/subcategories")]
        public async Task<IActionResult> GetSubCategories(int n)
        {
            var subs = await _shopDb.SubCategories
                .Where(sc => sc.CategoryN == n && sc.IsActive)
                .OrderBy(sc => sc.Name)
                .Select(sc => new { sc.N, sc.Name })
                .ToListAsync();
            return Json(subs);
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
        //  HERO SLIDES
        // ═══════════════════════════════════════════════════════════

        [HttpGet("hero-slides")]
        public async Task<IActionResult> HeroSlides()
        {
            var slides = await _heroSlideRepo.GetAllAsync();
            return View(slides);
        }

        [HttpGet("hero-slides/create")]
        public async Task<IActionResult> HeroSlideCreate()
        {
            ViewBag.HeroSlideCategories = await _categoryRepo.GetAllAsync();
            return View(new AdminHeroSlideFormViewModel());
        }

        [HttpPost("hero-slides/create")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> HeroSlideCreate(AdminHeroSlideFormViewModel vm)
        {
            if (!ModelState.IsValid)
            {
                ViewBag.HeroSlideCategories = await _categoryRepo.GetAllAsync();
                return View(vm);
            }

            var slide = new HeroSlide
            {
                ImageUrl     = vm.ImageUrl,
                EyebrowText  = vm.EyebrowText,
                Title        = vm.Title,
                Subtitle     = vm.Subtitle,
                ButtonText   = vm.ButtonText,
                ButtonUrl    = vm.ButtonUrl,
                DisplayOrder = vm.DisplayOrder,
                IsActive     = vm.IsActive,
            };
            await _heroSlideRepo.AddAsync(slide);

            TempData["Success"] = $"Слайдът \"{slide.Title}\" е добавен успешно.";
            return RedirectToAction(nameof(HeroSlides));
        }

        [HttpGet("hero-slides/edit/{n}")]
        public async Task<IActionResult> HeroSlideEdit(int n)
        {
            var slide = await _heroSlideRepo.GetByNAsync(n);
            if (slide == null) return NotFound();

            var vm = new AdminHeroSlideFormViewModel
            {
                N            = slide.N,
                ImageUrl     = slide.ImageUrl,
                EyebrowText  = slide.EyebrowText,
                Title        = slide.Title,
                Subtitle     = slide.Subtitle,
                ButtonText   = slide.ButtonText,
                ButtonUrl    = slide.ButtonUrl,
                DisplayOrder = slide.DisplayOrder,
                IsActive     = slide.IsActive,
            };
            ViewBag.HeroSlideCategories = await _categoryRepo.GetAllAsync();
            return View(vm);
        }

        [HttpPost("hero-slides/edit/{n}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> HeroSlideEdit(AdminHeroSlideFormViewModel vm)
        {
            if (!ModelState.IsValid)
            {
                ViewBag.HeroSlideCategories = await _categoryRepo.GetAllAsync();
                return View(vm);
            }

            var slide = await _heroSlideRepo.GetByNAsync(vm.N);
            if (slide == null) return NotFound();

            slide.ImageUrl     = vm.ImageUrl;
            slide.EyebrowText  = vm.EyebrowText;
            slide.Title        = vm.Title;
            slide.Subtitle     = vm.Subtitle;
            slide.ButtonText   = vm.ButtonText;
            slide.ButtonUrl    = vm.ButtonUrl;
            slide.DisplayOrder = vm.DisplayOrder;
            slide.IsActive     = vm.IsActive;

            await _heroSlideRepo.UpdateAsync(slide);

            TempData["Success"] = $"Слайдът \"{slide.Title}\" е обновен успешно.";
            return RedirectToAction(nameof(HeroSlides));
        }

        [HttpPost("hero-slides/toggle/{n}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> HeroSlideToggle(int n)
        {
            await _heroSlideRepo.ToggleActiveAsync(n);
            TempData["Success"] = "Статусът на слайда е обновен.";
            return RedirectToAction(nameof(HeroSlides));
        }

        [HttpPost("hero-slides/upload-image")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> HeroSlideUploadImage(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { error = "Не е избран файл." });

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            var allowed = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
            if (!allowed.Contains(ext))
                return BadRequest(new { error = "Позволени формати: JPG, PNG, GIF, WEBP." });

            if (file.Length > 5 * 1024 * 1024)
                return BadRequest(new { error = "Файлът е твърде голям (max 5 MB)." });

            var dir = Path.Combine(_env.WebRootPath, "images", "uploads");
            Directory.CreateDirectory(dir);

            // If the file was already uploaded (e.g. user selected it from the uploads folder),
            // reuse the existing file instead of creating a duplicate with a new GUID.
            var originalName = Path.GetFileName(file.FileName);
            var existingPath = Path.Combine(dir, originalName);
            if (System.IO.File.Exists(existingPath))
                return Ok(new { url = $"/images/uploads/{originalName}" });

            var fileName = $"{Guid.NewGuid()}{ext}";
            var filePath = Path.Combine(dir, fileName);

            using var stream = new FileStream(filePath, FileMode.Create);
            await file.CopyToAsync(stream);

            return Ok(new { url = $"/images/uploads/{fileName}" });
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

        // ═══════════════════════════════════════════════════════════
        //  PROMOTIONS
        // ═══════════════════════════════════════════════════════════

        [HttpGet("promotions")]
        public async Task<IActionResult> Promotions()
        {
            var promotions = await _promotionRepo.GetAllAsync();
            return View(promotions);
        }

        [HttpGet("promotions/create")]
        public async Task<IActionResult> PromotionCreate()
        {
            var vm = new AdminPromotionFormViewModel
            {
                ProductRows = await LoadProductRowsAsync(
                    existing: new Dictionary<int, ProductPromotion>(),
                    startDate: DateTime.Today,
                    endDate:   DateTime.Today.AddDays(7),
                    excludePromotionN: null),
            };
            return View(vm);
        }

        [HttpPost("promotions/create")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PromotionCreate(AdminPromotionFormViewModel vm)
        {
            await ValidatePromotionAsync(vm, excludePromotionN: null);

            if (!ModelState.IsValid)
            {
                await RehydrateProductRowsAsync(vm, existingMap: new Dictionary<int, ProductPromotion>(), excludePromotionN: null);
                return View(vm);
            }

            var currentUserN = await GetCurrentUserNAsync();

            var promotion = new Promotion
            {
                Name        = vm.Name,
                StartDate   = vm.StartDate,
                EndDate     = vm.EndDate,
                Type        = vm.Type,
                IsActive    = vm.IsActive,
                UserCreated = currentUserN,
                CreatedAt   = DateTime.Now,
            };
            _shopDb.Promotions.Add(promotion);
            await _shopDb.SaveChangesAsync();

            foreach (var row in vm.ProductRows.Where(r => r.Included))
            {
                _shopDb.ProductPromotions.Add(new ProductPromotion
                {
                    ProductN        = row.ProductN,
                    PromotionN      = promotion.N,
                    OriginalPrice   = row.OriginalPrice,
                    PromoPrice      = row.PromoPrice,
                    DiscountPercent = row.DiscountPercent,
                    Note            = string.IsNullOrWhiteSpace(row.Note) ? null : row.Note.Trim(),
                });
            }
            await _shopDb.SaveChangesAsync();

            TempData["Success"] = $"Промоцията \"{promotion.Name}\" е създадена успешно.";
            return RedirectToAction(nameof(Promotions));
        }

        [HttpGet("promotions/edit/{n}")]
        public async Task<IActionResult> PromotionEdit(int n)
        {
            var promotion = await _promotionRepo.GetByNWithProductsAsync(n);
            if (promotion == null) return NotFound();

            var existingMap = promotion.ProductPromotions.ToDictionary(pp => pp.ProductN);

            var vm = new AdminPromotionFormViewModel
            {
                N           = promotion.N,
                Name        = promotion.Name,
                StartDate   = promotion.StartDate,
                EndDate     = promotion.EndDate,
                Type        = promotion.Type,
                IsActive    = promotion.IsActive,
                ProductRows = await LoadProductRowsAsync(existingMap, promotion.StartDate, promotion.EndDate, promotion.N),
            };
            return View(vm);
        }

        [HttpPost("promotions/edit/{n}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PromotionEdit(AdminPromotionFormViewModel vm)
        {
            await ValidatePromotionAsync(vm, excludePromotionN: vm.N);

            var promotion = await _shopDb.Promotions
                .Include(pr => pr.ProductPromotions)
                .FirstOrDefaultAsync(pr => pr.N == vm.N);
            if (promotion == null) return NotFound();

            if (!ModelState.IsValid)
            {
                var existingMap = promotion.ProductPromotions.ToDictionary(pp => pp.ProductN);
                await RehydrateProductRowsAsync(vm, existingMap, excludePromotionN: vm.N);
                return View(vm);
            }

            promotion.Name      = vm.Name;
            promotion.StartDate = vm.StartDate;
            promotion.EndDate   = vm.EndDate;
            promotion.Type      = vm.Type;
            promotion.IsActive  = vm.IsActive;

            var incomingByProductN = vm.ProductRows.Where(r => r.Included).ToDictionary(r => r.ProductN);
            var existingByProductN = promotion.ProductPromotions.ToDictionary(pp => pp.ProductN);

            // Изтрий премахнатите
            foreach (var pp in existingByProductN.Values.Where(pp => !incomingByProductN.ContainsKey(pp.ProductN)).ToList())
                _shopDb.ProductPromotions.Remove(pp);

            // Обнови или добави
            foreach (var row in incomingByProductN.Values)
            {
                var note = string.IsNullOrWhiteSpace(row.Note) ? null : row.Note.Trim();
                if (existingByProductN.TryGetValue(row.ProductN, out var pp))
                {
                    pp.OriginalPrice   = row.OriginalPrice;
                    pp.PromoPrice      = row.PromoPrice;
                    pp.DiscountPercent = row.DiscountPercent;
                    pp.Note            = note;
                }
                else
                {
                    _shopDb.ProductPromotions.Add(new ProductPromotion
                    {
                        ProductN        = row.ProductN,
                        PromotionN      = promotion.N,
                        OriginalPrice   = row.OriginalPrice,
                        PromoPrice      = row.PromoPrice,
                        DiscountPercent = row.DiscountPercent,
                        Note            = note,
                    });
                }
            }

            await _shopDb.SaveChangesAsync();

            TempData["Success"] = $"Промоцията \"{promotion.Name}\" е обновена успешно.";
            return RedirectToAction(nameof(Promotions));
        }

        [HttpPost("promotions/toggle/{n}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PromotionToggle(int n)
        {
            var promotion = await _shopDb.Promotions.FindAsync(n);
            if (promotion == null) return NotFound();

            promotion.IsActive = !promotion.IsActive;
            await _shopDb.SaveChangesAsync();

            TempData["Success"] = promotion.IsActive
                ? $"\"{promotion.Name}\" е активирана."
                : $"\"{promotion.Name}\" е деактивирана.";
            return RedirectToAction(nameof(Promotions));
        }

        [HttpPost("promotions/delete/{n}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PromotionDelete(int n)
        {
            await _promotionRepo.DeleteAsync(n);
            TempData["Success"] = "Промоцията е изтрита.";
            return RedirectToAction(nameof(Promotions));
        }

        // ─── Помощни методи за промоции ──────────────────────────

        /// <summary>Валидира периода, редовете и припокриването на промоция.</summary>
        private async Task ValidatePromotionAsync(AdminPromotionFormViewModel vm, int? excludePromotionN)
        {
            if (vm.EndDate < vm.StartDate)
                ModelState.AddModelError(nameof(vm.EndDate), "Крайната дата трябва да е след началната.");

            var included = vm.ProductRows.Where(r => r.Included).ToList();
            if (included.Count == 0)
                ModelState.AddModelError(string.Empty, "Изберете поне един продукт.");

            // PromoPrice > 0 AND PromoPrice < OriginalPrice
            for (int i = 0; i < vm.ProductRows.Count; i++)
            {
                var row = vm.ProductRows[i];
                if (!row.Included) continue;

                if (row.OriginalPrice <= 0)
                {
                    ModelState.AddModelError($"ProductRows[{i}].OriginalPrice",
                        "Оригиналната цена трябва да е положителна.");
                }
                if (row.PromoPrice <= 0)
                {
                    ModelState.AddModelError($"ProductRows[{i}].PromoPrice",
                        "Крайната цена трябва да е по-голяма от 0 €.");
                }
                else if (row.PromoPrice >= row.OriginalPrice)
                {
                    ModelState.AddModelError($"ProductRows[{i}].PromoPrice",
                        "Крайната цена трябва да е по-малка от оригиналната.");
                }
            }

            if (included.Count > 0 && vm.EndDate >= vm.StartDate)
            {
                var productNs = included.Select(r => r.ProductN).ToList();
                var overlaps  = await GetOverlappingPromotionsAsync(productNs, vm.StartDate, vm.EndDate, excludePromotionN);

                foreach (var (productN, promoName) in overlaps)
                {
                    var idx = vm.ProductRows.FindIndex(r => r.ProductN == productN);
                    if (idx >= 0)
                    {
                        ModelState.AddModelError($"ProductRows[{idx}].Included",
                            $"Продуктът вече е в активна промоция \"{promoName}\" през този период.");
                    }
                }
            }
        }

        /// <summary>
        /// За списък от продукти връща двойки (ProductN, PromotionName) — първата активна промоция
        /// (различна от excludePromotionN), която се припокрива с [start, end].
        /// </summary>
        private async Task<List<(int ProductN, string PromotionName)>> GetOverlappingPromotionsAsync(
            IEnumerable<int> productNs, DateTime start, DateTime end, int? excludePromotionN)
        {
            var set = productNs.ToHashSet();
            var rows = await _shopDb.ProductPromotions
                .Include(pp => pp.Promotion)
                .Where(pp => set.Contains(pp.ProductN)
                          && pp.Promotion.IsActive
                          && (excludePromotionN == null || pp.PromotionN != excludePromotionN)
                          && pp.Promotion.StartDate <= end
                          && pp.Promotion.EndDate   >= start)
                .Select(pp => new { pp.ProductN, pp.Promotion.Name })
                .ToListAsync();

            return rows
                .GroupBy(r => r.ProductN)
                .Select(g => (g.Key, g.First().Name))
                .ToList();
        }

        /// <summary>
        /// Строи списъка с редове от ВСИЧКИ активни продукти — markирайки вече включените в текущата
        /// промоция (existing) и показвайки warning за продукти, попадащи в друга припокриваща промоция.
        /// </summary>
        private async Task<List<AdminPromotionProductRowViewModel>> LoadProductRowsAsync(
            IDictionary<int, ProductPromotion> existing,
            DateTime startDate,
            DateTime endDate,
            int? excludePromotionN)
        {
            var products = await _shopDb.Products
                .Where(p => p.IsActive)
                .OrderBy(p => p.Name)
                .ToListAsync();

            var overlaps = (await GetOverlappingPromotionsAsync(
                products.Select(p => p.N), startDate, endDate, excludePromotionN))
                .ToDictionary(x => x.ProductN, x => x.PromotionName);

            return products.Select(p =>
            {
                var row = new AdminPromotionProductRowViewModel
                {
                    ProductN     = p.N,
                    ProductName  = p.Name,
                    ImageUrl     = p.ImageUrl,
                    CurrentPrice = p.Price,
                };

                if (existing.TryGetValue(p.N, out var pp))
                {
                    row.Included        = true;
                    row.OriginalPrice   = pp.OriginalPrice;
                    row.PromoPrice      = pp.PromoPrice;
                    row.DiscountPercent = pp.DiscountPercent;
                    row.Note            = pp.Note;
                }
                else
                {
                    row.OriginalPrice = p.Price;
                }

                if (overlaps.TryGetValue(p.N, out var otherName))
                    row.OverlappingPromotionName = otherName;

                return row;
            }).ToList();
        }

        /// <summary>
        /// След неуспешен POST — попълва в ProductRows прозводните полета (ProductName, ImageUrl,
        /// CurrentPrice, OverlappingPromotionName), които не се пост-ват обратно от формата.
        /// </summary>
        private async Task RehydrateProductRowsAsync(
            AdminPromotionFormViewModel vm,
            IDictionary<int, ProductPromotion> existingMap,
            int? excludePromotionN)
        {
            var products = await _shopDb.Products
                .Where(p => p.IsActive)
                .OrderBy(p => p.Name)
                .ToListAsync();

            var byN = products.ToDictionary(p => p.N);
            var postedByN = vm.ProductRows.ToDictionary(r => r.ProductN);

            var overlaps = (await GetOverlappingPromotionsAsync(
                products.Select(p => p.N), vm.StartDate, vm.EndDate, excludePromotionN))
                .ToDictionary(x => x.ProductN, x => x.PromotionName);

            var rebuilt = new List<AdminPromotionProductRowViewModel>(products.Count);
            foreach (var p in products)
            {
                AdminPromotionProductRowViewModel row;
                if (postedByN.TryGetValue(p.N, out var posted))
                {
                    row = posted;
                }
                else
                {
                    row = new AdminPromotionProductRowViewModel
                    {
                        ProductN      = p.N,
                        OriginalPrice = p.Price,
                    };
                }

                row.ProductName  = p.Name;
                row.ImageUrl     = p.ImageUrl;
                row.CurrentPrice = p.Price;

                if (overlaps.TryGetValue(p.N, out var otherName))
                    row.OverlappingPromotionName = otherName;

                rebuilt.Add(row);
            }
            vm.ProductRows = rebuilt;
        }

        /// <summary>Връща N на текущо логнатия админ; 1 при неуспех (safe fallback).</summary>
        private async Task<int> GetCurrentUserNAsync()
        {
            var username = User.Identity?.Name;
            if (string.IsNullOrEmpty(username)) return 1;

            var user = await _userService.GetUserByUsernameAsync(username);
            return user?.N ?? 1;
        }
    }
}
