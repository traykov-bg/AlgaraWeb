using Algara.Identity.Models;

namespace Algara.Identity.Data
{
    public class UserRole
    {
        public int UserN { get; set; }
        public int RoleN { get; set; }

        // EF populates these required navigations when the related entities are loaded.
        public ApplicationUser User { get; set; } = null!;
        public ApplicationRole Role { get; set; } = null!;
    }
}
