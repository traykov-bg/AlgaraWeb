using System.ComponentModel.DataAnnotations;
using Algara.Data.Models;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Algara.Web.ViewModels.Admin;

public abstract class CatalogEditForm
{
    public int? N { get; set; }
    public string? RowVersion { get; set; }
    [BindNever, ValidateNever] public List<PartnerCatalogChange> History { get; set; } = [];
}

public sealed class CompanyForm : CatalogEditForm, IValidatableObject
{
    [Required, StringLength(200)] public string Name { get; set; } = string.Empty;
    [StringLength(50)] public string? LegalForm { get; set; }
    [Required, RegularExpression("[A-Za-z]{2}")] public string CountryCode { get; set; } = "BG";
    [Required, RegularExpression("EIK|BULSTAT|FOREIGN")] public string RegistrationType { get; set; } = "EIK";
    [StringLength(64)] public string? RegistrationNumber { get; set; }
    public bool IsVatRegistered { get; set; }
    [StringLength(64)] public string? VatNumber { get; set; }
    [StringLength(200)] public string? Representative { get; set; }
    [StringLength(50)] public string? Phone { get; set; }
    [EmailAddress, StringLength(256)] public string? Email { get; set; }
    [StringLength(500)] public string? AccountingContact { get; set; }
    public CatalogAddress Address { get; set; } = new();
    public CatalogAddress CorrespondenceAddress { get; set; } = new();
    [StringLength(4000)] public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!PartnerCatalogRules.TryPhone(Phone, out _))
            yield return new("Въведете един телефон: български с начална 0 или международен с + / 00.", [nameof(Phone)]);
    }
}

public sealed class PartnerForm : CatalogEditForm, IValidatableObject
{
    [StringLength(32)] public string? Code { get; set; }
    [Required(ErrorMessage = "Въведете име на обекта."), StringLength(400)] public string SiteName { get; set; } = string.Empty;
    public int? CompanyN { get; set; }
    [Range(0, int.MaxValue)] public int GroupN { get; set; }
    [StringLength(50)] public string? Phone { get; set; }
    [EmailAddress, StringLength(256)] public string? Email { get; set; }
    [StringLength(200)] public string? ContactPerson { get; set; }
    public CatalogAddress Address { get; set; } = new();
    public CatalogAddress DeliveryAddress { get; set; } = new();
    [StringLength(4000)] public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    [BindNever, ValidateNever] public List<SelectListItem> Companies { get; set; } = [];
    [BindNever, ValidateNever] public List<SelectListItem> Groups { get; set; } = [];
    [BindNever, ValidateNever] public bool CanViewLinkedAccounts { get; set; }
    [BindNever, ValidateNever] public List<PartnerLinkedAccountViewModel> LinkedAccounts { get; set; } = [];
    [BindNever, ValidateNever] public int LinkedAccountsPage { get; set; } = 1;
    [BindNever, ValidateNever] public bool HasMoreLinkedAccounts { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!string.IsNullOrWhiteSpace(Code) && !PartnerCatalogRules.IsCode(Code.Trim()))
            yield return new("Кодът трябва да съдържа до 32 цифри. Водещите нули се запазват.", [nameof(Code)]);
        if (!PartnerCatalogRules.TryPartnerPhone(Phone, out _))
            yield return new("Въведете един телефон: български с начална 0 или международен с + / 00. При липса оставете празно или въведете 0.", [nameof(Phone)]);
    }
}

public sealed class PartnerGroupForm : CatalogEditForm
{
    [Required, StringLength(150)] public string Name { get; set; } = string.Empty;
    public int? ParentN { get; set; }
    [BindNever, ValidateNever] public List<SelectListItem> Parents { get; set; } = [];
}

public sealed record PartnerGroupRow(int N, string Name, string Path, int Depth, int Count, int? ParentN = null, string RowVersion = "");

public sealed class PartnerListModel
{
    public string MoveSelectionKey { get; set; } = string.Empty;
    public List<Partner> Items { get; set; } = [];
    public List<PartnerGroupRow> Groups { get; set; } = [];
    public List<SelectListItem> Companies { get; set; } = [];
    public string? Search { get; set; }
    public bool? Active { get; set; }
    public int? CompanyN { get; set; }
    public int GroupN { get; set; }
    public bool IncludeChildren { get; set; }
    public bool All { get; set; }
    public int Page { get; set; }
    public int TotalPages { get; set; }
    public int Total { get; set; }
}

public sealed class CompanyListModel
{
    public List<Company> Items { get; set; } = [];
    public string? Search { get; set; }
    public bool? Active { get; set; }
    public int Page { get; set; }
    public int TotalPages { get; set; }
    public int Total { get; set; }
}
