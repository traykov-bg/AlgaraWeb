using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Algara.Web.ViewModels.Admin;

public class CustomerManagementActionForm
{
    [BindRequired, Required(ErrorMessage = "Презаредете досието преди действието.")]
    public string? ExpectedAccountVersion { get; set; }
    [Required(ErrorMessage = "Запишете основанието за действието."), StringLength(2000)]
    public string? Reason { get; set; }
}

public sealed class CustomerPasswordResetForm : CustomerManagementActionForm
{
    [Required(ErrorMessage = "Въведете Вашата текуща парола."), StringLength(1024), DataType(DataType.Password)]
    public string? ActorPassword { get; set; }
    [Required(ErrorMessage = "Въведете новата парола."), StringLength(1024, MinimumLength = 6,
        ErrorMessage = "Новата парола трябва да е между 6 и 1024 символа."), DataType(DataType.Password)]
    public string? NewPassword { get; set; }
    [Required(ErrorMessage = "Повторете новата парола."), StringLength(1024), DataType(DataType.Password),
        Compare(nameof(NewPassword), ErrorMessage = "Новата парола и потвърждението не съвпадат.")]
    public string? ConfirmPassword { get; set; }
}

public sealed class CustomerProfileEditForm : CustomerManagementActionForm
{
    [Required(ErrorMessage = "Въведете име."), StringLength(100)]
    public string FirstName { get; set; } = string.Empty;
    [Required(ErrorMessage = "Въведете фамилия."), StringLength(100)]
    public string LastName { get; set; } = string.Empty;
    [Phone(ErrorMessage = "Въведете валиден телефон."), StringLength(40)]
    public string? PhoneNumber { get; set; }
}

public sealed class CustomerAddressEditForm : CustomerManagementActionForm
{
    public int? N { get; set; }
    [BindRequired, Required(ErrorMessage = "Презаредете адресите преди запис.")]
    public string? ExpectedAddressesVersion { get; set; }
    [Required(ErrorMessage = "Въведете име."), StringLength(100)]
    public string FirstName { get; set; } = string.Empty;
    [Required(ErrorMessage = "Въведете фамилия."), StringLength(100)]
    public string LastName { get; set; } = string.Empty;
    [Phone(ErrorMessage = "Въведете валиден телефон."), StringLength(40)]
    public string? PhoneNumber { get; set; }
    [Required(ErrorMessage = "Въведете имейл."), EmailAddress(ErrorMessage = "Въведете валиден имейл."), StringLength(256)]
    public string Email { get; set; } = string.Empty;
    [Required(ErrorMessage = "Въведете адрес."), StringLength(250)]
    public string AddressLine1 { get; set; } = string.Empty;
    [StringLength(250)] public string? AddressLine2 { get; set; }
    [Required(ErrorMessage = "Въведете град."), StringLength(100)]
    public string City { get; set; } = string.Empty;
    [StringLength(20)] public string? PostalCode { get; set; }
    [Required(ErrorMessage = "Въведете държава."), StringLength(100)]
    public string Country { get; set; } = "България";
    public bool IsDefault { get; set; }
}

public sealed class CustomerAddressActionForm : CustomerManagementActionForm
{
    [Range(1, int.MaxValue)] public int AddressN { get; set; }
    [BindRequired, Required(ErrorMessage = "Презаредете адресите преди действието.")]
    public string? ExpectedAddressesVersion { get; set; }
}

public sealed class CustomerSessionActionForm : CustomerManagementActionForm
{
    [Range(1, int.MaxValue)] public int SessionN { get; set; }
    [BindRequired, Required(ErrorMessage = "Презаредете сесиите преди действието.")]
    public string? ExpectedSessionVersion { get; set; }
}

public sealed class CustomerSessionViewModel
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public string DeviceInfo { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool IsCurrent { get; set; }
    public string Version { get; set; } = string.Empty;
}
