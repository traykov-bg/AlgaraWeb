using System.ComponentModel.DataAnnotations;

namespace Algara.Web.ViewModels
{
    public class RegisterViewModel
    {
        [Required(ErrorMessage = "Имейлът е задължителен")]
        [EmailAddress(ErrorMessage = "Невалиден имейл адрес")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Паролата е задължителна")]
        [DataType(DataType.Password)]
        [MinLength(6)]
        public string Password { get; set; } = string.Empty;

        [Required(ErrorMessage = "Потвърдете паролата")]
        [DataType(DataType.Password)]
        [Compare("Password", ErrorMessage = "Паролите не съвпадат")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}