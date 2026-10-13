using System.ComponentModel.DataAnnotations.Schema;

namespace Algara.Data.Models;

public class Company
{
    public int N { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? LegalForm { get; set; }
    public string CountryCode { get; set; } = "BG";
    public string RegistrationType { get; set; } = "EIK";
    public string? RegistrationNumber { get; set; }
    public string? NormalizedRegistrationNumber { get; set; }
    public bool IsVatRegistered { get; set; }
    public string? VatNumber { get; set; }
    public string? Representative { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? AccountingContact { get; set; }
    public CatalogAddress Address { get; set; } = new();
    public CatalogAddress CorrespondenceAddress { get; set; } = new();
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public byte[] RowVersion { get; set; } = [];

    [NotMapped]
    public string DisplayName => string.Join(" ", new[] { Name, LegalForm }.Where(v => !string.IsNullOrEmpty(v)));
}
