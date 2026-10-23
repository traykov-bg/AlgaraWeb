using System.ComponentModel.DataAnnotations;
using Algara.Data.Models;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Algara.Web.ViewModels.Admin;

public sealed class CustomerDossierViewModel
{
    public CustomerProfileViewModel Customer { get; set; } = new();
    public List<CustomerAddressViewModel> Addresses { get; set; } = [];
    public int AddressPage { get; set; } = 1;
    public bool HasMoreAddresses { get; set; }
    public CustomerPartnerViewModel? Partner { get; set; }
    public List<CustomerOrderViewModel> Orders { get; set; } = [];
    public int OrderPage { get; set; } = 1;
    public bool HasMoreOrders { get; set; }
    public List<CustomerHistoryViewModel> History { get; set; } = [];
    public int HistoryPage { get; set; } = 1;
    public bool HasMoreHistory { get; set; }
    public bool CanViewOrders { get; set; }
    public bool CanViewPartners { get; set; }
    public bool CanViewHistory { get; set; }
    public bool CanAddNotes { get; set; }
    public bool CanManageLinks { get; set; }
    public bool CanSendAccountEmails { get; set; }
    public bool CanViewEmailHistory { get; set; }
    public CustomerAccountEmailForm EmailForm { get; set; } = new();
    public List<AccountEmailHistoryViewModel> EmailHistory { get; set; } = [];
    public string? PartnerSearch { get; set; }
    public int PartnerPage { get; set; } = 1;
    public bool HasMorePartners { get; set; }
    public List<CustomerPartnerViewModel> PartnerChoices { get; set; } = [];
    public CustomerPartnerViewModel? SelectedPartner { get; set; }
    public CustomerNoteForm NoteForm { get; set; } = new();
    public CustomerLinkForm LinkForm { get; set; } = new();
    public CustomerUnlinkForm UnlinkForm { get; set; } = new();
}

// Deliberately separate from ApplicationUser: credentials and session identifiers
// must never become properties of the dossier's view model.
public sealed class CustomerProfileViewModel
{
    public int N { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public bool IsActive { get; set; }
    public bool EmailConfirmed { get; set; }
    public bool RequireEmailConfirmation { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginDate { get; set; }
    public DateTime? LockoutUntil { get; set; }
    public int FailedLoginAttempts { get; set; }
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? PostalCode { get; set; }
    public string? Country { get; set; }
}

public sealed class CustomerAddressViewModel
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string Email { get; set; } = string.Empty;
    public string AddressLine1 { get; set; } = string.Empty;
    public string? AddressLine2 { get; set; }
    public string City { get; set; } = string.Empty;
    public string? PostalCode { get; set; }
    public string Country { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
}

public sealed class CustomerPartnerViewModel
{
    public int N { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? CompanyName { get; set; }
    public string? RegistrationNumber { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? City { get; set; }
    public string? Street { get; set; }
    public bool IsActive { get; set; }
    public bool AllowAccountLinks { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class CustomerOrderViewModel
{
    public int N { get; set; }
    public OrderStatus Status { get; set; }
    public decimal TotalAmount { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class CustomerHistoryViewModel
{
    public long N { get; set; }
    public string Action { get; set; } = string.Empty;
    public string ActorName { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
    public string? Reason { get; set; }
    public string? VerificationMethod { get; set; }
    public string? Note { get; set; }
    public string? BeforePartner { get; set; }
    public string? AfterPartner { get; set; }
}

public sealed class CustomerNoteForm
{
    [BindRequired, Required] public string? ExpectedAccountVersion { get; set; }
    [Required(ErrorMessage = "Въведете бележка."), StringLength(4000)]
    public string? Note { get; set; }
}

public sealed class CustomerAccountEmailForm
{
    [BindRequired, Required] public string? ExpectedAccountVersion { get; set; }
    [Required(ErrorMessage = "Запишете основание за изпращането."), StringLength(2000)]
    public string? Reason { get; set; }
}

public sealed class AccountEmailHistoryViewModel
{
    public string ActorName { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
    public string? DeliveryState { get; set; }
}

public sealed class CustomerLinkForm
{
    [Range(1, int.MaxValue)] public int PartnerN { get; set; }
    [BindRequired, Required] public string? ExpectedAccountVersion { get; set; }
    [BindRequired, Required] public string? ExpectedLinkVersion { get; set; }
    [BindRequired, Required] public string? ExpectedPartnerVersion { get; set; }
    public bool ConfirmedSpecificPartner { get; set; }
    public bool ConfirmedCustomerRequest { get; set; }
    [Required(ErrorMessage = "Изберете начин на проверка."), RegularExpression("in_person|known_contact|documented_request")]
    public string? VerificationMethod { get; set; }
    [Required(ErrorMessage = "Запишете основанието и извършената проверка."), StringLength(2000)]
    public string? Reason { get; set; }
}

public sealed class CustomerUnlinkForm
{
    [BindRequired, Required] public string? ExpectedAccountVersion { get; set; }
    [BindRequired, Required] public string? ExpectedLinkVersion { get; set; }
    [Required(ErrorMessage = "Запишете основание за прекратяване на връзката."), StringLength(2000)]
    public string? Reason { get; set; }
}

public sealed class PartnerLinkedAccountViewModel
{
    public int N { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}
