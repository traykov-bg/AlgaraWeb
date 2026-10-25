using Algara.Data.Data;
using Algara.Data.Repositories;
using Algara.Identity.Data;
using Algara.Web.Data;
using Algara.Web.Localization;
using Algara.Identity.Models;
using Algara.Identity.Services;
using Algara.Identity.Authorization;
using Algara.Web.Authorization;
using Microsoft.AspNetCore.Authorization;
using Algara.Utils;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NLog;
using NLog.Web;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Algara.Web.Services.Email;

var logger = LogManager.Setup().LoadConfigurationFromFile().GetCurrentClassLogger();
logger.Info("Application is starting...");
try
{
    var builder = WebApplication.CreateBuilder(args);

    // Конфигуриране на NLog
    builder.Logging.ClearProviders();
    builder.Host.UseNLog();

    // Add services to the container.
    builder.Services.AddControllersWithViews(options => options.Conventions.Add(new StaffPermissionConvention()));
    builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
    builder.Services.Configure<RequestLocalizationOptions>(CatalogLocalization.Configure);

    // Зареждане на ConnectionString
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        throw new InvalidOperationException("ConnectionStrings:DefaultConnection must be configured.");
    }

    var provider = builder.Configuration["DatabaseProvider"];

    // Регистриране на правилния DatabaseHelper според настройките
    if (provider == "Sybase")
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The Sybase OleDb provider requires Windows.");
        }

        builder.Services.AddSingleton<IDatabaseHelper>(new SybaseDatabaseHelper(connectionString));
    }
    else if (provider == "MSSQL")
    {
        builder.Services.AddSingleton<IDatabaseHelper>(sp => new MSSQLDatabaseHelper(connectionString));
    }
    else
    {
        throw new Exception("Unsupported database provider!");
    }
    builder.Services.AddDbContext<IdentityDbContext>(options =>
        options.UseSqlServer(connectionString, b => b.MigrationsAssembly("Algara.Web")));

    builder.Services.AddDbContext<ShopDbContext>(options =>
        options.UseSqlServer(connectionString, b => b
            .MigrationsAssembly("Algara.Web")
            .MigrationsHistoryTable("__EFMigrationsHistory_Shop")));

    // Регистрираме IHttpContextAccessor (нужен за UserService)
    builder.Services.AddHttpContextAccessor();

    // Регистрираме UserService
    builder.Services.AddScoped<IUserService, UserService>();
    builder.Services.AddScoped<StaffAccessService>();
    builder.Services.AddScoped<AccessManagementService>();
    builder.Services.AddScoped<Algara.Web.Services.CustomerDossierService>();
    builder.Services.AddScoped<Algara.Web.Services.ICustomerManagementService, Algara.Web.Services.CustomerManagementService>();
    builder.Services.AddScoped<Algara.Web.Services.Email.SmtpSettingsService>();
    builder.Services.AddSingleton<Algara.Web.Services.Email.ISmtpSecretProtector, Algara.Web.Services.Email.SmtpSecretProtector>();
    builder.Services.AddSingleton<Algara.Web.Services.Email.ISmtpConnectionProbe, Algara.Web.Services.Email.SmtpConnectionProbe>();
    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddSingleton<EmailOutboxFactory>();
    builder.Services.AddSingleton<IAccountEmailSender, AccountSmtpEmailSender>();
    builder.Services.AddScoped<EmailDeliveryReadiness>();
    builder.Services.AddScoped<EmailOutboxDispatcher>();
    builder.Services.AddScoped<IAccountEmailService, AccountEmailService>();
    builder.Services.AddHostedService<EmailOutboxWorker>();
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.AddPolicy("AccountEmail", context => RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30, Window = TimeSpan.FromMinutes(10), QueueLimit = 0, AutoReplenishment = true
            }));
    });
    builder.Services.AddSingleton<IAuthorizationPolicyProvider, StaffPolicyProvider>();
    builder.Services.AddScoped<IAuthorizationHandler, StaffAuthorizationHandler>();

    // Регистрираме Shop repositories
    builder.Services.AddScoped<IProductRepository, ProductRepository>();
    builder.Services.AddScoped<Algara.Web.Services.IProductGalleryWriteLock, Algara.Web.Services.ProductGalleryWriteLock>();
    builder.Services.AddScoped<ICategoryRepository, CategoryRepository>();
    builder.Services.AddScoped<ISubCategoryRepository, SubCategoryRepository>();
    builder.Services.AddScoped<IOrderRepository, OrderRepository>();
    builder.Services.AddScoped<IHeroSlideRepository, HeroSlideRepository>();
    builder.Services.AddScoped<IPromotionRepository, PromotionRepository>();

    builder.Services.Configure<IdentityOptions>(options =>
    {
        // Настройки за паролите
        options.Password.RequireDigit = false;
        options.Password.RequiredLength = 6;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireLowercase = false;

        // Заключване на акаунт при грешни опити за влизане
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
        options.Lockout.MaxFailedAccessAttempts = 10;
        options.Lockout.AllowedForNewUsers = true;

        // Настройки за потребителските имена
        options.User.RequireUniqueEmail = true;
    });

    // Активиране на автентикацията
    builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.Cookie.Name = "AlgaraAuth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
        options.SlidingExpiration = true;
        options.Events.OnValidatePrincipal = async context =>
        {
            var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
            var dbContext = context.HttpContext.RequestServices.GetRequiredService<IdentityDbContext>(); 
            var userStore = context.HttpContext.RequestServices.GetRequiredService<IUserStore<ApplicationUser>>();
            var userId = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            var sessionId = context.Principal?.FindFirstValue("SessionId");

            logger.LogInformation("🔍 Проверка на сесия за UserID: {UserId}, SessionID: {SessionId}", userId, sessionId);

            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(sessionId))
            {
                logger.LogWarning("⛔ Липсва UserID или SessionId. Излизане...");
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync();
                return;
            }

            var user = await userStore.FindByIdAsync(userId, CancellationToken.None);

            if (user == null || !user.IsActive || (user.RequireEmailConfirmation && !user.EmailConfirmed))
            {
                logger.LogWarning("⛔ Потребителят не е намерен. Излизане...");
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync();
                return;
            }

            var userService = context.HttpContext.RequestServices.GetRequiredService<IUserService>();
            var session = await userService.GetActiveSessionAsync(user.N, sessionId);

            if (session == null || user.SecurityStamp != context.Principal?.FindFirstValue("SecurityStamp"))
            {
                logger.LogWarning("⛔ Неуспешна проверка! Сесията е невалидна или изтекла.");
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync();
            }
            else
            {
                logger.LogInformation("✅ SessionId е валиден, продължаваме сесията.");
            }
        };
    });

    builder.Services.AddAuthorization();
    builder.Services.AddScoped<IUserStore<ApplicationUser>, UserStore>();

    builder.Services.AddControllers()
        .AddNewtonsoftJson(options =>
        {
            options.SerializerSettings.Formatting = Newtonsoft.Json.Formatting.Indented;
        });

    var app = builder.Build();

    var dbHelper = app.Services.GetRequiredService<IDatabaseHelper>();
    await dbHelper.EnsureTablesExist();

    // Seed данни (само в Development)
    if (app.Environment.IsDevelopment())
    {
        using var scope = app.Services.CreateScope();
        var shopDb = scope.ServiceProvider.GetRequiredService<ShopDbContext>();
        await ShopDbSeeder.SeedAsync(shopDb);

        var identityDb = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var userSvc    = scope.ServiceProvider.GetRequiredService<IUserService>();
        await AdminSeeder.SeedAsync(identityDb, userSvc);
    }

    // Configure the HTTP request pipeline.
    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Home/Error");
        // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
        app.UseHsts();
    }

    app.UseHttpsRedirection();
    app.UseStaticFiles();

    // Персонализирани страници за HTTP грешки (404, 403 и др.)
    app.UseStatusCodePagesWithReExecute("/Home/HttpError/{0}");

    // Security headers
    app.Use(async (context, next) =>
    {
        context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
        context.Response.Headers.Append("X-Frame-Options", "DENY");
        context.Response.Headers.Append("X-XSS-Protection", "1; mode=block");
        context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
        await next();
    });

    app.UseRouting();

    app.UseRequestLocalization();

    app.UseAuthentication();
    app.UseAuthorization();
    app.UseRateLimiter();

    app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}");

    app.Run();
}
// EF tools deliberately stop the host after resolving services; let that signal propagate.
catch (Exception ex) when (ex is not HostAbortedException)
{
    logger.Error(ex, "Application terminated unexpectedly");
    throw;
}
finally
{
    LogManager.Shutdown();
}
