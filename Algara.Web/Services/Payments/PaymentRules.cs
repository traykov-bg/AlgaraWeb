using System.Text.Json;
using Algara.Data.Models;
using Algara.Data.Data;
using Microsoft.EntityFrameworkCore;
namespace Algara.Web.Services.Payments;
public static class PaymentRules
{
 public static DateOnly Today(TimeProvider clock) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById("Europe/Sofia")).DateTime);
 public static bool RequiresDeposit(Order order)
 {
  if(order.OrderItems.Any(i => i.IndividualManufacture || i.RequiresManufacturing)) return true;
  if(string.IsNullOrEmpty(order.DeliverySnapshot)) return false;
  try { using var doc=JsonDocument.Parse(order.DeliverySnapshot); return !doc.RootElement.TryGetProperty("Method",out var m) || m.ValueKind!=JsonValueKind.Number || !m.TryGetInt32(out var value) || value is <0 or >5 or 1; }
  catch(JsonException) { return true; }
 }
 public static decimal Deposit(Order o, bool exempt) => o.DepositRequired && !exempt ? Math.Ceiling(o.TotalAmount * (o.DepositPercent ?? 50m))/100m : 0m;
 public static bool Money(decimal value) => value>0 && value<=999999999m && decimal.Round(value,2)==value;
 public static async Task InitializeAsync(ShopDbContext db, Order order, CancellationToken ct)
 {
  order.DepositPercent=await db.PaymentPolicies.Where(p=>p.N==1).Select(p=>p.DepositPercent).SingleAsync(ct);
  order.DepositRequired=RequiresDeposit(order);
 }
 public static async Task<Partner?> PartnerAsync(ShopDbContext db, Order o, CancellationToken ct)
 {
  var partnerN=o.PartnerN;
  if(!partnerN.HasValue && o.UserN.HasValue) partnerN=await db.PartnerAccountLinks.Where(l=>l.UserN==o.UserN).Select(l=>l.PartnerN).SingleOrDefaultAsync(ct);
  return partnerN.HasValue ? await db.Partners.SingleOrDefaultAsync(p=>p.N==partnerN && p.IsActive,ct) : null;
 }
 public static string State(Order o, decimal paid, DateOnly today) => o.Status==OrderStatus.Cancelled ? (paid>0?"RefundDue":"Cancelled") : paid>o.TotalAmount?"Overpaid":paid==o.TotalAmount?"Paid":o.PaymentDueOn<today?"Overdue":paid>0?"Partial":"Unpaid";
}
