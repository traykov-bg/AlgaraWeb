using Algara.Identity.Data;
using Algara.Identity.Models;

namespace Algara.Web.Data
{
    public static class AdminSeeder
    {
        public static async Task SeedAsync(IdentityDbContext identityDb, IUserService userService)
        {
            // 1. Създай Admin роля ако не съществува (вече е N=1 в dev БД)
            if (!identityDb.Roles.Any(r => r.Name == "Admin"))
            {
                identityDb.Roles.Add(new ApplicationRole
                {
                    Name        = "Admin",
                    Description = "Системен администратор"
                });
                await identityDb.SaveChangesAsync();
            }

            // 2. Създай default admin потребител само за чисто нова среда
            if (!identityDb.Users.Any(u => u.UserName == "admin@algara.bg"))
            {
                await userService.RegisterUserAsync("admin@algara.bg", "admin@algara.bg", "Admin@algara1");
                await userService.AddUserToRoleAsync("admin@algara.bg", "Admin");
            }
        }
    }
}
