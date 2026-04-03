using Algara.Identity.Data;
using System.ComponentModel.DataAnnotations.Schema;

namespace Algara.Identity.Models
{
    public class ApplicationUser : IdentityUser
    {
        public string FullName { get; set; }
        public string SecurityStamp { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// Runtime cache на ролите — зарежда се от UserRoles таблицата, не се пази в Users.
        /// </summary>
        [NotMapped]
        public List<string> Roles { get; set; } = new();

        public string? LastLoginSessionId { get; set; }
        public DateTime? LastLoginDate { get; set; }
        public int FailedLoginAttempts { get; set; } = 0;
        public DateTime? LockoutUntil { get; set; }

        /// <summary>
        /// Предпочитан часови пояс на потребителя (Windows timezone ID, например "FLE Standard Time").
        /// Задава се от страницата с профилни настройки.
        /// Null = часовият пояс не е конфигуриран — използва се offset-ът от последната сесия.
        /// </summary>
        public string? PreferredTimeZoneId { get; set; }

        public ICollection<UserSession> UserSessions { get; set; } = new List<UserSession>();
    }
}
