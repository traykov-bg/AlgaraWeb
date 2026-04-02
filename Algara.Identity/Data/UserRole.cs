using Algara.Identity.Models;

namespace Algara.Identity.Data
{
    public class UserRole
    {
        public int UserN { get; set; }
        public int RoleN { get; set; }

        public ApplicationUser User { get; set; }
        public ApplicationRole Role { get; set; }
    }
}
