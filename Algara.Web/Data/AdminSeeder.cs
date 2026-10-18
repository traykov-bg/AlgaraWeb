using Algara.Identity.Data;
using Algara.Identity.Models;
using Algara.Identity.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Algara.Web.Data
{
    public static class AdminSeeder
    {
        public static async Task SeedAsync(IdentityDbContext identityDb, IUserService userService)
        {
            // 1. Роля "Admin"
            if (!identityDb.Roles.Any(r => r.Name == "Admin"))
            {
                identityDb.Roles.Add(new ApplicationRole
                {
                    Name        = "Admin",
                    Description = "Системен администратор"
                });
                await identityDb.SaveChangesAsync();
            }

            // 2. Роля "User" (задава се автоматично на всеки нов потребител)
            if (!identityDb.Roles.Any(r => r.Name == "User"))
            {
                identityDb.Roles.Add(new ApplicationRole
                {
                    Name        = "User",
                    Description = "Регистриран потребител"
                });
                await identityDb.SaveChangesAsync();
            }

            // 3. Default admin потребител (само за чисто нова среда)
            if (!await identityDb.UserRoles.AnyAsync(r => r.Role.Name == StaffRoles.Admin && r.User.IsActive)
                && !identityDb.Users.Any(u => u.UserName == "admin@algara.bg"))
            {
                await userService.RegisterUserAsync("admin@algara.bg", "admin@algara.bg", "Admin@algara1");
                await new AccessManagementService(identityDb).BootstrapAdminAsync("admin@algara.bg");
            }
        }
    }
}
