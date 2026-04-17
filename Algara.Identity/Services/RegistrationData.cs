namespace Algara.Identity.Services
{
    /// <summary>
    /// Данни за регистрация — включва и consent информацията за audit trail.
    /// </summary>
    public class RegistrationData
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string? PhoneNumber { get; set; }

        public bool MarketingConsent { get; set; }

        /// <summary>
        /// Версия на T&C и Privacy Policy, които потребителят приема.
        /// </summary>
        public string PolicyVersion { get; set; } = "1.0";

        public string? IpAddress { get; set; }
        public string? UserAgent { get; set; }
    }
}
