using System.ComponentModel.DataAnnotations;
using Algara.Web.Services.Cart;
using Algara.Web.Services.Shipping;
using Algara.Web.Services.Shipping.Carriers;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace Algara.Web.ViewModels;

public sealed class DeliveryQuoteForm
{
    [EnumDataType(typeof(DeliveryMethod))] public DeliveryMethod Method { get; set; }
    [StringLength(100)] public string? City { get; set; }
    [StringLength(10)] public string? PostalCode { get; set; }
    [StringLength(500)] public string? Address { get; set; }
    [StringLength(40)] public string? OfficeCode { get; set; }
    [BindNever, ValidateNever] public CartSnapshot Cart { get; set; } = new();
    [BindNever, ValidateNever] public IReadOnlyList<CourierOffice> Offices { get; set; } = [];
    [BindNever, ValidateNever] public DeliveryQuote? Quote { get; set; }
    [BindNever, ValidateNever] public string? ErrorKey { get; set; }
}
