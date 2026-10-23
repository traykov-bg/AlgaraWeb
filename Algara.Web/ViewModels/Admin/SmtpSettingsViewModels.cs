using System.ComponentModel.DataAnnotations;
using Algara.Identity.Models;
using Algara.Web.Services.Email;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace Algara.Web.ViewModels.Admin;

public sealed class SmtpSettingsForm
{
    [BindRequired, Required(ErrorMessage = "Презаредете настройките преди запис.")]
    public string? ExpectedVersion { get; set; }

    [Required(ErrorMessage = "Въведете SMTP сървър."), StringLength(253)]
    public string Host { get; set; } = string.Empty;

    [Range(1, 65535, ErrorMessage = "Портът трябва да е между 1 и 65535.")]
    public int Port { get; set; } = 587;

    [BindRequired, EnumDataType(typeof(SmtpSecurityMode), ErrorMessage = "Изберете защитена SMTP връзка.")]
    public SmtpSecurityMode SecurityMode { get; set; } = SmtpSecurityMode.StartTls;

    [Required(ErrorMessage = "Въведете потребителско име за SMTP."), StringLength(256)]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "Въведете имейл на подателя."), EmailAddress(ErrorMessage = "Въведете валиден имейл на подателя."), StringLength(256)]
    public string FromEmail { get; set; } = string.Empty;

    [StringLength(200)]
    public string? FromName { get; set; }

    [StringLength(2048)]
    public string? PublicSiteUrl { get; set; }

    public bool Enabled { get; set; }

    [DataType(DataType.Password), StringLength(1024, ErrorMessage = "Паролата не може да надвишава 1024 знака.")]
    public string? Password { get; set; }

    public bool RemovePassword { get; set; }

    [BindNever, ValidateNever] public bool HasPassword { get; set; }
    [BindNever, ValidateNever] public bool PasswordReadable { get; set; }
    [BindNever, ValidateNever] public IReadOnlyList<SmtpSettingsHistoryEntry> History { get; set; } = [];
    [BindNever, ValidateNever] public bool CanCheckConnection { get; set; }
    [BindNever, ValidateNever] public string? DeliverySetupWarning { get; set; }
}

public sealed class SmtpConnectionCheckForm
{
    [BindRequired, Required(ErrorMessage = "Презаредете настройките преди проверка.")]
    public string? ExpectedVersion { get; set; }
}
