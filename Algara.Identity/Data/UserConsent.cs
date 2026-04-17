using Algara.Identity.Models;

namespace Algara.Identity.Data
{
    /// <summary>
    /// Audit trail на дадени/оттеглени съгласия — за доказване пред КЗЛД (чл. 7(1) GDPR).
    /// Всяко действие (дадено или оттеглено съгласие) се записва като отделен ред,
    /// никога не се ъпдейтват съществуващи записи.
    /// </summary>
    public class UserConsent
    {
        public long Id { get; set; }
        public int UserN { get; set; }

        /// <summary>
        /// Типът съгласие: Terms, Privacy, Marketing, Age (18+), ...
        /// </summary>
        public string ConsentType { get; set; } = string.Empty;

        /// <summary>
        /// true = дадено съгласие, false = оттеглено.
        /// </summary>
        public bool Granted { get; set; }

        /// <summary>
        /// Версия на документа/политиката към момента на съгласието (напр. "1.0").
        /// Позволява да знаем на коя версия T&C/Privacy е дал съгласие потребителят.
        /// </summary>
        public string? PolicyVersion { get; set; }

        public DateTime ConsentedAt { get; set; } = DateTime.UtcNow;
        public string? IpAddress { get; set; }
        public string? UserAgent { get; set; }

        public ApplicationUser? User { get; set; }
    }

    public static class ConsentTypes
    {
        public const string Terms = "Terms";
        public const string Privacy = "Privacy";
        public const string Marketing = "Marketing";
        public const string Age18 = "Age18";
    }
}
