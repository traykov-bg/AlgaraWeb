using System.ComponentModel.DataAnnotations;
using Algara.Data.Models;
using Algara.Web.Services.Cart;
using Algara.Web.Services.Shipping;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
namespace Algara.Web.ViewModels.Admin;

public sealed class StaffOrderForm
{
    [Required, StringLength(20)] public string Channel { get; set; } = "Phone";
    [Range(1, int.MaxValue)] public int PartnerN { get; set; }
    public bool Storefront { get; set; }
    [Range(1, int.MaxValue)] public int? CopiedFromOrderN { get; set; }
    public int? CustomerUserN { get; set; }
    [Range(0, 1000000)] public int ScrollY { get; set; }
    [BindNever, ValidateNever] public int ActiveAccountCount { get; set; }
    [Required, StringLength(121)] public string ContactName { get; set; } = "";
    [EmailAddress, StringLength(256)] public string? Email { get; set; }
    [Required, Phone, StringLength(20)] public string Phone { get; set; } = "";
    [Required, StringLength(500)] public string Reason { get; set; } = "";
    [StringLength(2000)] public string? Comment { get; set; }
    [StringLength(1000)] public string? InvoiceDetails { get; set; }
    public SplitDestination Destination { get; set; } = new();
    [MaxLength(30)] public List<CartEntry> Lines { get; set; } = [];
    [StringLength(10000)] public string? SubmissionToken { get; set; }
    [StringLength(10000)] public string? PreviewToken { get; set; }
    public bool CustomerAgreed { get; set; }
    [StringLength(100)] public string? ProductSearch { get; set; }
    [StringLength(100)] public string? PartnerSearch { get; set; }
    [BindNever, ValidateNever] public CartSnapshot Cart { get; set; } = new();
    [BindNever, ValidateNever] public DeliveryQuote? Quote { get; set; }
    [BindNever, ValidateNever] public string? Error { get; set; }
    [BindNever, ValidateNever] public Partner? Partner { get; set; }
    [BindNever, ValidateNever] public List<Partner> Partners { get; set; } = [];
    [BindNever, ValidateNever] public List<Product> Products { get; set; } = [];
    [BindNever, ValidateNever] public List<StaffOrderCustomer> Customers { get; set; } = [];
    [BindNever, ValidateNever] public IReadOnlyList<int> EnabledCouriers { get; set; } = [];
}
public sealed record StaffOrderCustomer(int N, string Name, string? ContactName = null, string? Phone = null, string? Email = null);
