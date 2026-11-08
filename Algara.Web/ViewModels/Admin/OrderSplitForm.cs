using System.ComponentModel.DataAnnotations;
using Algara.Data.Models;
using Algara.Web.Services.Shipping;
using Algara.Web.Services.Shipping.Carriers;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
namespace Algara.Web.ViewModels.Admin;
public sealed class SplitDestination
{
 public DeliveryMethod Method { get; set; }
 [StringLength(100)] public string? City { get; set; }
 [StringLength(20)] public string? PostalCode { get; set; }
 [StringLength(500)] public string? Address { get; set; }
 [StringLength(500)] public string? AddressDetails { get; set; }
 [StringLength(40)] public string? OfficeCode { get; set; }
 [BindNever, ValidateNever] public IReadOnlyList<CourierOffice> Offices { get; set; } = [];
}
public sealed class OrderSplitForm
{
 public string Version { get; set; } = "";
 public Dictionary<int,int> Quantities { get; set; } = [];
 public SplitDestination Remaining { get; set; } = new();
 public SplitDestination Separated { get; set; } = new();
 [StringLength(500), Required] public string Reason { get; set; } = "";
 public bool CustomerAgreed { get; set; }
 public string? PreviewToken { get; set; }
 [BindNever, ValidateNever] public IReadOnlyList<int> EnabledCouriers { get; set; } = [];
 [BindNever, ValidateNever] public Order Order { get; set; } = null!;
 [BindNever, ValidateNever] public DeliveryQuote? RemainingQuote { get; set; }
 [BindNever, ValidateNever] public DeliveryQuote? SeparatedQuote { get; set; }
 [BindNever, ValidateNever] public string? Error { get; set; }
}
