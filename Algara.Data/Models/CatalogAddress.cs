using System.ComponentModel.DataAnnotations;

namespace Algara.Data.Models;

// Separate owned values; never a reference to the account's delivery addresses.
public class CatalogAddress
{
    [StringLength(100)] public string? Country { get; set; }
    [StringLength(20)] public string? PostalCode { get; set; }
    [StringLength(150)] public string? City { get; set; }
    [StringLength(300)] public string? Street { get; set; }
    [StringLength(30)] public string? Building { get; set; }
    [StringLength(30)] public string? Entrance { get; set; }
    [StringLength(30)] public string? Floor { get; set; }
    [StringLength(30)] public string? Apartment { get; set; }
    [StringLength(500)] public string? Details { get; set; }
}
