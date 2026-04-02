using Algara.Identity.Models;

namespace Algara.Identity.Data
{
    public class UserSession
    {
        public int Id { get; set; }
        public int UserN { get; set; }
        public string SessionId { get; set; }
        public DateTime CreatedAt { get; set; }
        public string DeviceInfo { get; set; } // Например "Chrome, Windows 11"
        public bool IsActive { get; set; }

        /// <summary>
        /// Разлика в минути от UTC на часовата зона на потребителя при логване.
        /// Пример: UTC+2 (България) → +120, UTC-5 → -300
        /// </summary>
        public int? TimeZoneOffset { get; set; }

        public ApplicationUser User { get; set; }
    }
}
