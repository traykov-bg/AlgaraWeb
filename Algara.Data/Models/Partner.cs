using System.ComponentModel.DataAnnotations.Schema;

namespace Algara.Data.Models;

public enum PartnerKind { Person = 0, CompanySite = 1, GeneralCustomer = 2 }

public class Partner
{
    public int N { get; set; }
    public string Code { get; set; } = string.Empty;
    public PartnerKind Kind { get; set; }
    public string? FirstName { get; set; }
    public string? MiddleName { get; set; }
    public string? LastName { get; set; }
    public string SiteName { get; set; } = string.Empty;
    public string? NormalizedFirstName { get; set; }
    public string? NormalizedLastName { get; set; }
    public string NormalizedSiteName { get; set; } = string.Empty;
    public int? CompanyN { get; set; }
    public Company? Company { get; set; }
    public int GroupN { get; set; }
    public PartnerGroup? Group { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? ContactPerson { get; set; }
    public CatalogAddress Address { get; set; } = new();
    public CatalogAddress DeliveryAddress { get; set; } = new();
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public byte[] RowVersion { get; set; } = [];

    [NotMapped]
    public string DisplayName => SiteName;
}
