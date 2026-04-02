using Algara.Identity.Data;

namespace Algara.Identity.Models
{
    public class ApplicationUser : IdentityUser
    {
        // Тук можеш да добавиш допълнителни свойства при нужда
        public string FullName { get; set; }
        public string SecurityStamp { get; set; } = Guid.NewGuid().ToString();
        public List<string> Roles { get; set; } = new(); 
        public string? LastLoginSessionId { get; set; }
        public int FailedLoginAttempts { get; set; } = 0;
        public DateTime? LockoutUntil { get; set; }

        public ICollection<UserSession> UserSessions { get; set; } = new List<UserSession>();
    }
}
