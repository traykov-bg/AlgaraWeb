using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Algara.Web.ViewModels
{
    public class RegisterViewModel
    {
        [Required(ErrorMessage = "Името е задължително")]
        [StringLength(60, MinimumLength = 2, ErrorMessage = "Името трябва да е между 2 и 60 символа")]
        [DisplayName("Име")]
        public string FirstName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Фамилията е задължителна")]
        [StringLength(60, MinimumLength = 2, ErrorMessage = "Фамилията трябва да е между 2 и 60 символа")]
        [DisplayName("Фамилия")]
        public string LastName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Имейлът е задължителен")]
        [EmailAddress(ErrorMessage = "Невалиден имейл адрес")]
        [DisplayName("Имейл")]
        public string Email { get; set; } = string.Empty;

        [Phone(ErrorMessage = "Невалиден телефонен номер")]
        [StringLength(20, ErrorMessage = "Телефонът не може да е над 20 символа")]
        [DisplayName("Телефон")]
        public string? PhoneNumber { get; set; }

        [Required(ErrorMessage = "Паролата е задължителна")]
        [DataType(DataType.Password)]
        [MinLength(6, ErrorMessage = "Паролата трябва да е поне 6 символа")]
        [DisplayName("Парола")]
        public string Password { get; set; } = string.Empty;

        [Required(ErrorMessage = "Потвърдете паролата")]
        [DataType(DataType.Password)]
        [Compare("Password", ErrorMessage = "Паролите не съвпадат")]
        [DisplayName("Потвърди паролата")]
        public string ConfirmPassword { get; set; } = string.Empty;

        [MustBeTrue(ErrorMessage = "Трябва да потвърдите, че сте над 18 години")]
        public bool AgeConfirmed { get; set; }

        [MustBeTrue(ErrorMessage = "Трябва да приемете Общите условия и Политиката за поверителност")]
        public bool TermsAccepted { get; set; }

        /// <summary>
        /// Маркетингово съгласие — не е задължително (GDPR чл. 7: freely given).
        /// </summary>
        public bool MarketingConsent { get; set; }
    }
}
