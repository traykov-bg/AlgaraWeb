using System.Data;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Algara.Data.Data;
using Algara.Data.Models;
using Algara.Identity.Authorization;
using Algara.Identity.Data;
using Algara.Web.ViewModels.Admin;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
namespace Algara.Web.Services.Payments;
public sealed class PaymentService(ShopDbContext db, TimeProvider clock)
{
 public static bool Parse(string? text,out decimal value) => decimal.TryParse(text?.Trim().Replace(',', '.'),NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out value);
 public async Task<string?> WriteAsync(ClaimsPrincipal actor,string permission,Func<Task<string?>> write,CancellationToken ct)
 {
  try {
   await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
   await db.Database.ExecuteSqlRawAsync("""
    DECLARE @r int;
    EXEC @r=sys.sp_getapplock @Resource=N'Algara.AccessManagement',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=15000;
    IF @r<0 THROW 51004,'Payments busy.',1;
    """,ct);
   await using var identity=new IdentityDbContext(new DbContextOptionsBuilder<IdentityDbContext>().UseSqlServer(db.Database.GetDbConnection()).Options);
   await identity.Database.UseTransactionAsync(tx.GetDbTransaction(),ct);
   if(!await new StaffAccessService(identity).HasPermissionAsync(actor,permission,ct)) return "Нямате актуално право за това действие. / Permission denied.";
   db.ChangeTracker.Clear();
   var error=await write(); if(error!=null)return error;
   await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);return null;
  } catch(DbUpdateException) { db.ChangeTracker.Clear(); return "Записът е променен или вече съществува. Презаредете. / Reload and try again."; }
  catch(SqlException ex) when(ex.Number is 1205 or 1222 or 51004) { db.ChangeTracker.Clear();return "Обработва се друга промяна. Опитайте отново. / Please retry."; }
 }
 public Task<string?> RecordAsync(int n,PaymentForm f,ClaimsPrincipal actor,CancellationToken ct) => WriteAsync(actor,f.Kind==PaymentEntryKind.Receipt?PermissionCatalog.Payments.Record:PermissionCatalog.Payments.Correct,async()=> {
  // Recheck all permissions inside the same access-management transaction.
  await using var identity=new IdentityDbContext(new DbContextOptionsBuilder<IdentityDbContext>().UseSqlServer(db.Database.GetDbConnection()).Options);
  await identity.Database.UseTransactionAsync(db.Database.CurrentTransaction!.GetDbTransaction(),ct);
  var access=new StaffAccessService(identity);
  if(!await access.HasPermissionAsync(actor,PermissionCatalog.Payments.View,ct) || !await access.HasPermissionAsync(actor,PermissionCatalog.Orders.View,ct)) return "Нямате право за преглед. / Permission denied.";
  var actorId=actor.FindFirstValue(ClaimTypes.NameIdentifier)!;
  var previous=await db.OrderPayments.SingleOrDefaultAsync(p=>p.SubmissionId==f.SubmissionId,ct);
  if(previous!=null) return previous.OrderN==n && previous.ActorId==actorId ? null : "Невалиден идентификатор. / Invalid submission.";
  var o=await db.Orders.Include(o=>o.Payments).SingleOrDefaultAsync(o=>o.N==n,ct);
  if(o==null || Convert.ToBase64String(o.RowVersion)!=f.Version) return "Поръчката е променена. Презаредете. / Order changed; reload.";
  if(f.SubmissionId==Guid.Empty || !Parse(f.Amount,out var amount) || !PaymentRules.Money(amount)) return "Невалидна сума. / Invalid amount.";
  if(f.PaidOn<new DateOnly(2000,1,1) || f.PaidOn>PaymentRules.Today(clock) || string.IsNullOrWhiteSpace(f.Location) || f.Location.Length>200 || f.Note?.Length>1000) return "Проверете датата и мястото. / Check date and location.";
  var paid=o.Payments.Sum(p=>p.Amount);
  string methodName; int? methodN;
  if(f.Kind==PaymentEntryKind.Receipt) {
   if(o.Status==OrderStatus.Cancelled || amount>o.TotalAmount-paid) return "Сумата надвишава остатъка или поръчката е отказана. / Amount exceeds balance or order is cancelled.";
   var method=await db.PaymentMethods.SingleOrDefaultAsync(m=>m.N==f.MethodN && m.IsActive,ct);
   if(method==null)return "Изберете активен начин на плащане. / Select an active payment method.";
   methodName=method.Name;methodN=method.N;
  } else {
   if(f.Kind is not (PaymentEntryKind.Correction or PaymentEntryKind.Refund) || string.IsNullOrWhiteSpace(f.Note)) return "Посочете вид и причина за обратния запис. / A reversal needs a reason.";
   var original=o.Payments.SingleOrDefault(p=>p.N==f.ReversesN && (f.Kind==PaymentEntryKind.Correction ? p.Kind!=PaymentEntryKind.Allocation : p.Amount>0));
   if(original==null || amount>Math.Abs(original.Amount)-o.Payments.Where(p=>p.ReversesN==original.N).Sum(p=>Math.Abs(p.Amount))) return "Обратният запис надвишава наличната сума. / Reversal exceeds available amount.";
   amount=f.Kind==PaymentEntryKind.Correction && original.Amount<0?amount:-amount;
   if(amount<0 && -amount>paid || amount>0 && o.Status!=OrderStatus.Cancelled && amount>o.TotalAmount-paid) return "Корекцията надвишава остатъка. / Correction exceeds balance.";
   methodName=original.MethodName;methodN=original.MethodN;
   if(f.Kind==PaymentEntryKind.Refund) {
    var refundMethod=await db.PaymentMethods.SingleOrDefaultAsync(m=>m.N==f.MethodN && m.IsActive,ct);
    if(refundMethod==null)return "Изберете начин на възстановяване. / Select refund method.";
    methodName=refundMethod.Name;methodN=refundMethod.N;
   }

  }
  var now=clock.GetUtcNow().UtcDateTime;
  db.OrderPayments.Add(new OrderPayment{OrderN=n,SubmissionId=f.SubmissionId,Kind=f.Kind,Amount=amount,MethodN=methodN,MethodName=methodName,Location=f.Location.Trim(),PaidOn=f.PaidOn,RecordedAtUtc=now,ActorId=actorId,ActorName=actor.Identity?.Name??actorId,Note=f.Note?.Trim(),ReversesN=f.Kind==PaymentEntryKind.Receipt?null:f.ReversesN});
  o.UpdatedAt=now;db.Entry(o).Property(x=>x.UpdatedAt).IsModified=true;
  db.Set<OrderEvent>().Add(new OrderEvent{OrderN=n,Action="PaymentRecorded",ActorId=actorId,OccurredAtUtc=now,Status=o.Status,Details=JsonSerializer.Serialize(new { f.Kind,f.SubmissionId })});
  return null;
 },ct);
}
