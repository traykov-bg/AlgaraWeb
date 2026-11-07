using System.ComponentModel.DataAnnotations;
using Algara.Web.Services.Cart;
using Algara.Web.Services.Shipping;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace Algara.Web.ViewModels;

public sealed class CheckoutForm
{
    [Required, StringLength(60, MinimumLength = 2)] public string FirstName { get; set; } = "";
    [Required, StringLength(60, MinimumLength = 2)] public string LastName { get; set; } = "";
    [Required, EmailAddress, StringLength(256)] public string Email { get; set; } = "";
    [Required, Phone, StringLength(20)] public string Phone { get; set; } = "";
    [EnumDataType(typeof(DeliveryMethod))] public DeliveryMethod Method { get; set; }
    [StringLength(100)] public string? City { get; set; }
    [StringLength(10)] public string? PostalCode { get; set; }
    [StringLength(500)] public string? Address { get; set; }
    [StringLength(200)] public string? AddressDetails { get; set; }
    [StringLength(40)] public string? OfficeCode { get; set; }
    [StringLength(2000)] public string? Comment { get; set; }
    public bool InvoiceRequested { get; set; }
    [StringLength(1000)] public string? InvoiceDetails { get; set; }
    public bool CreateAccount { get; set; }
    [StringLength(1024)] public string? Password { get; set; }
    [StringLength(1024)] public string? ConfirmPassword { get; set; }
    public bool TermsAccepted { get; set; }
    [StringLength(10000)] public string? SubmissionToken { get; set; }
    [StringLength(10000)] public string? QuoteToken { get; set; }
    [BindNever, ValidateNever] public IReadOnlyList<int> EnabledCouriers { get; set; } = [];
    [BindNever, ValidateNever] public CartSnapshot Cart { get; set; } = new();
    [BindNever, ValidateNever] public IReadOnlyList<Algara.Web.Services.Shipping.Carriers.CourierOffice> Offices { get; set; } = [];
    [BindNever, ValidateNever] public string? ErrorKey { get; set; }
}
