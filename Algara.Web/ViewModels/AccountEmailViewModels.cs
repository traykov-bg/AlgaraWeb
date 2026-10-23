using System.ComponentModel.DataAnnotations;

namespace Algara.Web.ViewModels;

public sealed class AccountEmailRequestForm
{
    [Required(ErrorMessage = "Въведете имейл адрес."), EmailAddress(ErrorMessage = "Невалиден имейл адрес."), StringLength(256)]
    public string Email { get; set; } = string.Empty;
}

public class AccountEmailTokenForm
{
    [Required(ErrorMessage = "Отворете отново връзката от писмото."), StringLength(76)]
    [RegularExpression("[0-9a-f]{32}\\.[A-Za-z0-9_-]{43}", ErrorMessage = "Отворете отново връзката от писмото.")]
    public string Token { get; set; } = string.Empty;
}

public class ResetPasswordForm : AccountEmailTokenForm
{
    [Required(ErrorMessage = "Въведете парола."), StringLength(1024, MinimumLength = 6, ErrorMessage = "Паролата трябва да е между 6 и 1024 символа."), DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;
    [Required(ErrorMessage = "Потвърдете паролата."), Compare(nameof(Password), ErrorMessage = "Паролите не съвпадат."), DataType(DataType.Password)]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public sealed class AcceptInvitationForm : ResetPasswordForm
{
    [Required(ErrorMessage = "Въведете име."), StringLength(60, MinimumLength = 2, ErrorMessage = "Името трябва да е между 2 и 60 символа.")]
    public string FirstName { get; set; } = string.Empty;
    [Required(ErrorMessage = "Въведете фамилия."), StringLength(60, MinimumLength = 2, ErrorMessage = "Фамилията трябва да е между 2 и 60 символа.")]
    public string LastName { get; set; } = string.Empty;
    [Phone(ErrorMessage = "Невалиден телефон."), StringLength(20)]
    public string? PhoneNumber { get; set; }
    [MustBeTrue(ErrorMessage = "Приемете Общите условия и Политиката за поверителност.")]
    public bool TermsAccepted { get; set; }
    public bool MarketingConsent { get; set; }
}
