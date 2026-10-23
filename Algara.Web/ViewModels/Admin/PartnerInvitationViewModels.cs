using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Algara.Web.ViewModels.Admin;

public sealed class PartnerInvitationPage
{
    public int PartnerN { get; set; }
    public string PartnerName { get; set; } = string.Empty;
    public string PartnerCode { get; set; } = string.Empty;
    public bool PartnerActive { get; set; }
    public PartnerInvitationForm Form { get; set; } = new();
    public List<PartnerInvitationRow> Items { get; set; } = [];
    public int Page { get; set; }
    public bool HasMore { get; set; }
    public bool CanViewHistory { get; set; }
    public List<AccountEmailHistoryViewModel> History { get; set; } = [];
}

public sealed class PartnerInvitationForm
{
    [BindRequired, Required] public string? ExpectedPartnerVersion { get; set; }
    [Required(ErrorMessage = "Въведете имейла на клиента."), EmailAddress(ErrorMessage = "Невалиден имейл."), StringLength(256)]
    public string Email { get; set; } = string.Empty;
    [Algara.Web.ViewModels.MustBeTrue(ErrorMessage = "Потвърдете точния контрагент и обект.")]
    public bool ConfirmedSpecificPartner { get; set; }
    [Algara.Web.ViewModels.MustBeTrue(ErrorMessage = "Потвърдете провереното искане на клиента.")]
    public bool ConfirmedCustomerRequest { get; set; }
    [Required(ErrorMessage = "Изберете начин на проверка."), RegularExpression("in_person|known_contact|documented_request")]
    public string? VerificationMethod { get; set; }
    [Required(ErrorMessage = "Запишете основанието и извършената проверка."), StringLength(2000)]
    public string? Reason { get; set; }
}

public sealed class PartnerInvitationRow
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string? CreatedByName { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? ConsumedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public string? DeliveryState { get; set; }
    public string? Reason { get; set; }
    public string? VerificationMethod { get; set; }
}

public sealed class PartnerInvitationVersionForm
{
    [BindRequired, Required] public string? ExpectedVersion { get; set; }
}

public sealed class PartnerInvitationCancelForm
{
    [BindRequired, Required] public string? ExpectedVersion { get; set; }
    [Required(ErrorMessage = "Запишете основание за отмяна."), StringLength(2000)] public string? Reason { get; set; }
}
