using Algara.Identity.Data;
using System.ComponentModel.DataAnnotations.Schema;

namespace Algara.Identity.Models
{
    public class ApplicationUser : IdentityUser
    {
        public string FullName { get; set; } = string.Empty;
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? PhoneNumber { get; set; }
        public string? AddressLine1 { get; set; }
        public string? AddressLine2 { get; set; }
        public string? City { get; set; }
        public string? PostalCode { get; set; }
        public string? Country { get; set; }

        /// <summary>
        /// Текущо състояние на marketing съгласието — за бърз lookup и opt-out.
        /// Историята на дадените/оттеглените съгласия се пази в UserConsents таблицата.
        /// </summary>
        public bool MarketingConsent { get; set; } = false;

        public string SecurityStamp { get; set; } = Guid.NewGuid().ToString();

        public bool EmailConfirmed { get; set; }
        // Existing accounts retain access without inventing a historical verification event.
        public bool RequireEmailConfirmation { get; set; }
        public DateTime? EmailConfirmedAtUtc { get; set; }

        // Only service-imposed suspension is reversible; ordinary inactive/deleted users have no marker.
        public DateTime? AccessSuspendedAtUtc { get; set; }

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
        public ICollection<UserAddress> Addresses { get; set; } = new List<UserAddress>();
    }
}
