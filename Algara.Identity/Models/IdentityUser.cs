using Microsoft.AspNet.Identity;

namespace Algara.Identity.Models
{
    public class IdentityUser : IUser
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public int N { get; set; }
        public string UserName { get; set; }
        public string DisplayName { get; set; }
        public string Email { get; set; }
        public string PasswordHash { get; set; }
        public string Salt { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
