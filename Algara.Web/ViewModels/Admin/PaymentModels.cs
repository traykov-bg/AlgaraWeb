using System.ComponentModel.DataAnnotations;
using Algara.Data.Models;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
namespace Algara.Web.ViewModels.Admin;
public sealed class PaymentForm
{
 public string Version { get; set; } = "";
 public Guid SubmissionId { get; set; } = Guid.NewGuid();
 public PaymentEntryKind Kind { get; set; }
 [Required] public string Amount { get; set; } = "";
 public int? MethodN { get; set; }
 [Required,StringLength(200)] public string Location { get; set; } = "";
 public DateOnly PaidOn { get; set; }
 [StringLength(1000)] public string? Note { get; set; }
 public long? ReversesN { get; set; }
 [BindNever,ValidateNever] public Order Order { get; set; } = null!;
 [BindNever,ValidateNever] public List<PaymentMethod> Methods { get; set; } = [];
 [BindNever,ValidateNever] public decimal Paid { get; set; }
 [BindNever,ValidateNever] public decimal RequiredDeposit { get; set; }
}
public sealed class PaymentReportRow
{
 public int N { get; set; }
 public bool Tracked { get; set; }
 public string? ContactName { get; set; }
 public string? ContactPhone { get; set; }
 public OrderStatus Status { get; set; }
 public decimal Total { get; set; }
 public decimal Paid { get; set; }
 public DateOnly? Due { get; set; }
 public DateOnly? Delivered { get; set; }
}
public sealed class PaymentReport
{
 public string? Search { get; set; }
 public string? State { get; set; }
 public int? PartnerN { get; set; }
 public int? MethodN { get; set; }
 public DateOnly? From { get; set; }
 public DateOnly? To { get; set; }
 public int Page { get; set; }
 public int Pages { get; set; }
 public int Count { get; set; }
 public decimal Total { get; set; }
 public decimal Paid { get; set; }
 public decimal Outstanding { get; set; }
 public List<PaymentMethod> Methods { get; set; } = [];
 public List<PaymentReportRow> Rows { get; set; } = [];
}
public sealed class PaymentSettingsForm
{
 public string Version { get; set; } = "";
 [Required] public string DepositPercent { get; set; } = "50";
 [BindNever,ValidateNever] public List<PaymentMethod> Methods { get; set; } = [];
 [BindNever,ValidateNever] public PaymentMethod NewMethod { get; set; } = new();
}
public sealed class PartnerPaymentForm
{
 public int N { get; set; }
 public string Version { get; set; } = "";
 [Range(0,3650)] public int DeferredPaymentDays { get; set; }
 public bool DepositExempt { get; set; }
 [BindNever,ValidateNever] public string Name { get; set; } = "";
}
