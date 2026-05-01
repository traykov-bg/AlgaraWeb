using Algara.Data.Models;
using System.ComponentModel.DataAnnotations;

namespace Algara.Web.ViewModels
{
    public class ProfileViewModel
    {
        public ProfileDetailsViewModel Details { get; set; } = new();
        public ChangePasswordViewModel Password { get; set; } = new();
        public DeleteAccountViewModel DeleteAccount { get; set; } = new();
        public bool MarketingConsent { get; set; }
        public IReadOnlyList<ProfileOrderRowViewModel> CurrentOrders { get; set; } = Array.Empty<ProfileOrderRowViewModel>();
        public IReadOnlyList<ProfileOrderRowViewModel> OrderHistory { get; set; } = Array.Empty<ProfileOrderRowViewModel>();
        public string? StatusMessage { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class ProfileDetailsViewModel
    {
        [Required(ErrorMessage = "Името е задължително.")]
        [StringLength(100, ErrorMessage = "Името може да бъде до 100 символа.")]
        [Display(Name = "Име")]
        public string FirstName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Фамилията е задължителна.")]
        [StringLength(100, ErrorMessage = "Фамилията може да бъде до 100 символа.")]
        [Display(Name = "Фамилия")]
        public string LastName { get; set; } = string.Empty;

        [Phone(ErrorMessage = "Въведете валиден телефон.")]
        [StringLength(40, ErrorMessage = "Телефонът може да бъде до 40 символа.")]
        [Display(Name = "Телефон")]
        public string? PhoneNumber { get; set; }

        [StringLength(250, ErrorMessage = "Адресът може да бъде до 250 символа.")]
        [Display(Name = "Адрес")]
        public string? AddressLine1 { get; set; }

        [StringLength(250, ErrorMessage = "Допълнението към адреса може да бъде до 250 символа.")]
        [Display(Name = "Допълнение към адреса")]
        public string? AddressLine2 { get; set; }

        [StringLength(100, ErrorMessage = "Градът може да бъде до 100 символа.")]
        [Display(Name = "Град")]
        public string? City { get; set; }

        [StringLength(20, ErrorMessage = "Пощенският код може да бъде до 20 символа.")]
        [Display(Name = "Пощенски код")]
        public string? PostalCode { get; set; }

        [StringLength(100, ErrorMessage = "Държавата може да бъде до 100 символа.")]
        [Display(Name = "Държава")]
        public string? Country { get; set; }

        public string Email { get; set; } = string.Empty;
    }

    public class ChangePasswordViewModel
    {
        [Required(ErrorMessage = "Текущата парола е задължителна.")]
        [DataType(DataType.Password)]
        [Display(Name = "Текуща парола")]
        public string CurrentPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "Новата парола е задължителна.")]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "Паролата трябва да бъде поне 6 символа.")]
        [DataType(DataType.Password)]
        [Display(Name = "Нова парола")]
        public string NewPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "Потвърдете новата парола.")]
        [DataType(DataType.Password)]
        [Compare(nameof(NewPassword), ErrorMessage = "Паролите не съвпадат.")]
        [Display(Name = "Повтори новата парола")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public class DeleteAccountViewModel
    {
        [Required(ErrorMessage = "Въведете паролата си, за да потвърдите.")]
        [DataType(DataType.Password)]
        [Display(Name = "Парола")]
        public string Password { get; set; } = string.Empty;

        [Required(ErrorMessage = "Потвърждението е задължително.")]
        [Display(Name = "Потвърждавам")]
        public bool ConfirmDeletion { get; set; }
    }

    public class ProfileOrderRowViewModel
    {
        public int N { get; set; }
        public OrderStatus Status { get; set; }
        public decimal TotalAmount { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public int ItemCount { get; set; }
        public string StatusLabel { get; set; } = string.Empty;
        public string StatusCssClass { get; set; } = string.Empty;
    }

    public class ProfileOrderDetailViewModel
    {
        public Order Order { get; set; } = null!;
        public string StatusLabel { get; set; } = string.Empty;
        public string StatusCssClass { get; set; } = string.Empty;
    }
}
