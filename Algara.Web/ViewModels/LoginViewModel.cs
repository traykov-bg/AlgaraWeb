using System.ComponentModel.DataAnnotations;

namespace Algara.Web.ViewModels
{
    public class LoginViewModel
    {
        [Required(ErrorMessage = "Имейлът е задължителен")]
        [EmailAddress(ErrorMessage = "Невалиден имейл адрес")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Паролата е задължителна")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        public bool RememberMe { get; set; } = false;

        /// <summary>
        /// Разлика в минути от UTC — попълва се от JavaScript при зареждане на страницата.
        /// Пример: UTC+2 → +120
        /// </summary>
        public int? TimeZoneOffset { get; set; }
    }
}