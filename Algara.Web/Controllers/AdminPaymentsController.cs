using System.Security.Claims;
using System.Text.Json;
using Algara.Data.Data;
using Algara.Data.Models;
using Algara.Identity.Authorization;
using Algara.Identity.Data;
using Algara.Web.Authorization;
using Algara.Web.Services.Payments;
using Algara.Web.ViewModels.Admin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
namespace Algara.Web.Controllers;
[Microsoft.AspNetCore.Authorization.Authorize(Policy="Staff")]
[ResponseCache(Duration=0,Location=ResponseCacheLocation.None,NoStore=true)]
[Route("admin/payments")]
[RequirePermission("Payments.View","Orders.View")]
public sealed class AdminPaymentsController(ShopDbContext db,PaymentService service,TimeProvider clock) : Controller
{
 [HttpGet("")]
 public async Task<IActionResult> Index(string? search,string? state,int? partnerN,int? methodN,DateOnly? from,DateOnly? to,int page=1)
 {
  var ct=HttpContext.RequestAborted; var today=PaymentRules.Today(clock);
  var q=db.Orders.AsNoTracking().AsQueryable();
  if(!string.IsNullOrWhiteSpace(search)) {search=search.Trim();int.TryParse(search,out var n); q=q.Where(o=>o.N==n || (o.ContactName!=null && o.ContactName.Contains(search)) || (o.ContactPhone!=null && o.ContactPhone.Contains(search)));}
  if(partnerN.HasValue) q=q.Where(o=>o.PartnerN==partnerN);
  if(methodN.HasValue) q=q.Where(o=>o.Payments.Any(p=>p.MethodN==methodN));
  if(from.HasValue) q=q.Where(o=>o.PaymentDueOn>=from);
  if(to.HasValue) q=q.Where(o=>o.PaymentDueOn<=to);
  var rows=q.Select(o=>new PaymentReportRow{N=o.N,Tracked=o.DepositPercent!=null || o.Payments.Any(),ContactName=o.ContactName,ContactPhone=o.ContactPhone,Status=o.Status,Total=o.TotalAmount,Paid=o.Payments.Sum(p=>(decimal?)p.Amount)??0m,Due=o.PaymentDueOn,Delivered=o.DeliveredOn});
  rows=state switch {
   "Legacy"=>rows.Where(o=>!o.Tracked),
   "Unpaid"=>rows.Where(o=>o.Tracked && o.Status!=OrderStatus.Cancelled && o.Paid==0 && o.Total>0),
   "Partial"=>rows.Where(o=>o.Tracked && o.Status!=OrderStatus.Cancelled && o.Paid>0 && o.Paid<o.Total),
   "Paid"=>rows.Where(o=>o.Tracked && o.Status!=OrderStatus.Cancelled && o.Paid==o.Total),
   "Outstanding"=>rows.Where(o=>o.Tracked && o.Status!=OrderStatus.Cancelled && o.Paid<o.Total),
   "Overdue"=>rows.Where(o=>o.Tracked && o.Status!=OrderStatus.Cancelled && o.Paid<o.Total && o.Due<today),
   "RefundDue"=>rows.Where(o=>o.Status==OrderStatus.Cancelled && o.Paid>0),
   "Overpaid"=>rows.Where(o=>o.Tracked && o.Status!=OrderStatus.Cancelled && o.Paid>o.Total),
   _=>rows };
  var count=await rows.CountAsync(ct);var pages=Math.Max(1,(count+29)/30);page=Math.Clamp(page,1,pages);
  return View(new PaymentReport{Search=search,State=state,PartnerN=partnerN,MethodN=methodN,From=from,To=to,Page=page,Pages=pages,Count=count,
   Total=await rows.SumAsync(o=>(decimal?)o.Total,ct)??0m,Paid=await rows.SumAsync(o=>(decimal?)o.Paid,ct)??0m,
   Outstanding=await rows.Where(o=>o.Tracked && o.Status!=OrderStatus.Cancelled && o.Total>o.Paid).SumAsync(o=>(decimal?)(o.Total-o.Paid),ct)??0m,
   Rows=await rows.OrderByDescending(o=>o.N).Skip((page-1)*30).Take(30).ToListAsync(ct),Methods=await db.PaymentMethods.AsNoTracking().OrderBy(m=>m.Name).ToListAsync(ct)});
 }
 private async Task<bool> Fill(int n,PaymentForm f)
 {
  var o=await db.Orders.AsNoTracking().Include(o=>o.OrderItems).Include(o=>o.Payments).SingleOrDefaultAsync(o=>o.N==n);
  if(o==null)return false; f.Order=o;f.Methods=await db.PaymentMethods.AsNoTracking().Where(m=>m.IsActive).OrderBy(m=>m.Name).ToListAsync();f.Paid=o.Payments.Sum(p=>p.Amount);
  var partner=o.DepositExempt.HasValue?null:await PaymentRules.PartnerAsync(db,o,HttpContext.RequestAborted);
  if(o.DepositPercent==null) {o.DepositPercent=await db.PaymentPolicies.Select(p=>p.DepositPercent).SingleAsync();o.DepositRequired=PaymentRules.RequiresDeposit(o);}
  f.RequiredDeposit=PaymentRules.Deposit(o,o.DepositExempt??partner?.DepositExempt??false);return true;
 }
 [HttpGet("orders/{n:int}")]
 public async Task<IActionResult> Order(int n)
 {
  var f=new PaymentForm{PaidOn=PaymentRules.Today(clock)}; if(!await Fill(n,f))return NotFound();f.Version=Convert.ToBase64String(f.Order.RowVersion);return View(f);
 }
 [HttpPost("orders/{n:int}"),ValidateAntiForgeryToken]
 public async Task<IActionResult> Order(int n,PaymentForm f)
 {
  // An already posted final instalment remains idempotent even when the current balance is zero.
  if(f.SubmissionId!=Guid.Empty && await db.OrderPayments.AsNoTracking().AnyAsync(p=>p.SubmissionId==f.SubmissionId && p.OrderN==n && p.ActorId==User.FindFirstValue(ClaimTypes.NameIdentifier))) {
   var replayError=await service.RecordAsync(n,f,User,HttpContext.RequestAborted);
   if(replayError==null)return RedirectToAction(nameof(Order),new{n});
   ModelState.AddModelError("",replayError);if(!await Fill(n,f))return NotFound();return View(f);
  }
  if(!PaymentService.Parse(f.Amount,out var amount) || !PaymentRules.Money(amount))ModelState.AddModelError(nameof(f.Amount),"Въведете положителна сума с до два знака. / Enter a positive amount with up to two decimals.");
  if(f.PaidOn<new DateOnly(2000,1,1) || f.PaidOn>PaymentRules.Today(clock))ModelState.AddModelError(nameof(f.PaidOn),"Посочете действителна дата, не в бъдещето. / Enter a valid payment date.");
  if(f.Kind==PaymentEntryKind.Receipt && f.MethodN==null)ModelState.AddModelError(nameof(f.MethodN),"Изберете начин на плащане. / Select a method.");
  if(f.Kind is PaymentEntryKind.Correction or PaymentEntryKind.Refund) {
   if(string.IsNullOrWhiteSpace(f.Note))ModelState.AddModelError(nameof(f.Note),"Посочете причина. / Enter a reason.");
   if(!f.ReversesN.HasValue)ModelState.AddModelError(nameof(f.ReversesN),"Изберете оригинално плащане. / Select the original receipt.");
  }
  if(!await Fill(n,f))return NotFound();
  if(f.Kind is not (PaymentEntryKind.Receipt or PaymentEntryKind.Correction or PaymentEntryKind.Refund)) ModelState.AddModelError(nameof(f.Kind),"Невалидно действие. / Invalid action.");
  if(f.Kind!=PaymentEntryKind.Correction && !f.Methods.Any(m=>m.N==f.MethodN)) ModelState.AddModelError(nameof(f.MethodN),"Изберете активен начин. / Select an active method.");
  if(f.Kind==PaymentEntryKind.Receipt && amount>f.Order.TotalAmount-f.Paid) ModelState.AddModelError(nameof(f.Amount),"Сумата надвишава остатъка. / Amount exceeds balance.");
  if(f.Kind is PaymentEntryKind.Correction or PaymentEntryKind.Refund) {
   var original=f.Order.Payments.SingleOrDefault(p=>p.N==f.ReversesN && (f.Kind==PaymentEntryKind.Correction ? p.Kind!=PaymentEntryKind.Allocation : p.Amount>0));
   if(original==null) ModelState.AddModelError(nameof(f.ReversesN),"Изберете допустим оригинален запис. / Select an eligible original entry.");
   else if(amount>Math.Abs(original.Amount)-f.Order.Payments.Where(p=>p.ReversesN==original.N).Sum(p=>Math.Abs(p.Amount)) || (f.Kind==PaymentEntryKind.Refund || original.Amount>0) && amount>f.Paid || f.Kind==PaymentEntryKind.Correction && original.Amount<0 && f.Order.Status!=OrderStatus.Cancelled && amount>f.Order.TotalAmount-f.Paid) ModelState.AddModelError(nameof(f.Amount),"Сумата надвишава наличното за връщане или корекция. / Amount exceeds reversible balance.");
  }
  if(ModelState.IsValid) {
   var error=await service.RecordAsync(n,f,User,HttpContext.RequestAborted);
   if(error==null){TempData["Success"]="Плащането е записано. / Payment recorded.";return RedirectToAction(nameof(Order),new{n});}
   ModelState.AddModelError("",error);
  }
  if(!await Fill(n,f))return NotFound();return View(f);
 }
}

[Microsoft.AspNetCore.Authorization.Authorize(Policy="Staff")]
[ResponseCache(Duration=0,Location=ResponseCacheLocation.None,NoStore=true)]
[Route("admin/payment-settings")]
[RequirePermission("Settings.Manage")]
public sealed class AdminPaymentSettingsController(ShopDbContext db,PaymentService service) : Controller
{
 [HttpGet("")]
 public async Task<IActionResult> Index() {
  var p=await db.PaymentPolicies.AsNoTracking().SingleAsync();return View(new PaymentSettingsForm{Version=Convert.ToBase64String(p.RowVersion),DepositPercent=p.DepositPercent.ToString(System.Globalization.CultureInfo.InvariantCulture),Methods=await db.PaymentMethods.AsNoTracking().OrderBy(m=>m.N).ToListAsync()});
 }
 [HttpPost(""),ValidateAntiForgeryToken]
 public async Task<IActionResult> Index(PaymentSettingsForm f) {
  if(!PaymentService.Parse(f.DepositPercent,out var percent) || percent<=0 || percent>100 || decimal.Round(percent,2)!=percent)ModelState.AddModelError(nameof(f.DepositPercent),"Процентът трябва да е над 0 и до 100. / Percentage must be above 0 and at most 100.");
  if(ModelState.IsValid) {
   var error=await service.WriteAsync(User,PermissionCatalog.Settings.Manage,async()=>{
    var p=await db.PaymentPolicies.SingleAsync();if(Convert.ToBase64String(p.RowVersion)!=f.Version)return "Настройката е променена. Презаредете. / Reload changed settings.";
    var before=p.DepositPercent;p.DepositPercent=percent;Audit("Policy",1,new{Before=before,After=percent});return null;
   },HttpContext.RequestAborted);
   if(error==null)return RedirectToAction(nameof(Index));ModelState.AddModelError("",error);
  }
  f.Methods=await db.PaymentMethods.AsNoTracking().OrderBy(m=>m.N).ToListAsync();return View(f);
 }
 [HttpPost("method"),ValidateAntiForgeryToken]
 public async Task<IActionResult> Method(int n,string? name,string? englishName,bool active,string? version) {
  var error=await service.WriteAsync(User,PermissionCatalog.Settings.Manage,async()=>{
   if(!ModelState.IsValid || string.IsNullOrWhiteSpace(name)||name.Length>100||englishName?.Length>100)return "Въведете име до 100 знака. / Enter a name up to 100 characters.";
   var m=n==0?new PaymentMethod():await db.PaymentMethods.SingleOrDefaultAsync(m=>m.N==n);
   if(m==null || n!=0 && Convert.ToBase64String(m.RowVersion)!=version)return "Начинът е променен. Презаредете. / Reload changed method.";
   var before=new{m.Name,m.EnglishName,m.IsActive};m.Name=name.Trim();m.EnglishName=englishName?.Trim();m.IsActive=active;if(n==0)db.Add(m);
   await db.SaveChangesAsync();Audit("Method",m.N,new{Before=before,After=new{m.Name,m.EnglishName,m.IsActive}});return null;
  },HttpContext.RequestAborted);
  if(error==null)return RedirectToAction(nameof(Index));
  ModelState.AddModelError("MethodName"+n,error);
  var policy=await db.PaymentPolicies.AsNoTracking().SingleAsync();
  var form=new PaymentSettingsForm{Version=Convert.ToBase64String(policy.RowVersion),DepositPercent=policy.DepositPercent.ToString(System.Globalization.CultureInfo.InvariantCulture),Methods=await db.PaymentMethods.AsNoTracking().OrderBy(m=>m.N).ToListAsync()};
  var draft=n==0?form.NewMethod:form.Methods.SingleOrDefault(m=>m.N==n);
  if(draft!=null){draft.Name=name??"";draft.EnglishName=englishName;draft.IsActive=active;draft.RowVersion=Convert.TryFromBase64String(version??"",new byte[8],out var length)&&length==8?Convert.FromBase64String(version!):[];}
  return View("Index",form);
 }
 private void Audit(string type,int n,object details) => db.PartnerCatalogChanges.Add(new PartnerCatalogChange{EntityType="Payment"+type,EntityN=n,ActorId=User.FindFirstValue(ClaimTypes.NameIdentifier)!,ActorName=User.Identity?.Name??"",CreatedAtUtc=DateTime.UtcNow,AfterJson=JsonSerializer.Serialize(details)});
}

[Microsoft.AspNetCore.Authorization.Authorize(Policy="Staff")]
[ResponseCache(Duration=0,Location=ResponseCacheLocation.None,NoStore=true)]
[Route("admin/partners/{n:int}/payment-terms")]
[RequirePermission("Partners.View","Users.EditProfile")]
public sealed class AdminPartnerPaymentTermsController(ShopDbContext db,PaymentService service) : Controller
{
 [HttpGet("")]
 public async Task<IActionResult> Index(int n) {var p=await db.Partners.AsNoTracking().SingleOrDefaultAsync(p=>p.N==n);return p==null?NotFound():View(new PartnerPaymentForm{N=n,Name=p.SiteName,Version=Convert.ToBase64String(p.RowVersion),DepositExempt=p.DepositExempt,DeferredPaymentDays=p.DeferredPaymentDays});}
 [HttpPost(""),ValidateAntiForgeryToken]
 public async Task<IActionResult> Index(int n,PartnerPaymentForm f) {
  if(n!=f.N)return BadRequest();
  if(ModelState.IsValid) {
   var error=await service.WriteAsync(User,PermissionCatalog.Users.EditProfile,async()=>{
    await using var identity=new IdentityDbContext(new DbContextOptionsBuilder<IdentityDbContext>().UseSqlServer(db.Database.GetDbConnection()).Options);
    await identity.Database.UseTransactionAsync(db.Database.CurrentTransaction!.GetDbTransaction());
    if(!await new StaffAccessService(identity).HasPermissionAsync(User,PermissionCatalog.Partners.View))return "Нямате право за преглед на контрагента. / Permission denied.";
    var p=await db.Partners.SingleOrDefaultAsync(p=>p.N==n);
    if(p==null || Convert.ToBase64String(p.RowVersion)!=f.Version)return "Контрагентът е променен. Презаредете. / Reload changed partner.";
    var before=JsonSerializer.Serialize(new{p.DepositExempt,p.DeferredPaymentDays});p.DepositExempt=f.DepositExempt;p.DeferredPaymentDays=f.DeferredPaymentDays;
    db.PartnerCatalogChanges.Add(new PartnerCatalogChange{EntityType="Partner",EntityN=n,ActorId=User.FindFirstValue(ClaimTypes.NameIdentifier)!,ActorName=User.Identity?.Name??"",CreatedAtUtc=DateTime.UtcNow,BeforeJson=before,AfterJson=JsonSerializer.Serialize(new{p.DepositExempt,p.DeferredPaymentDays})});return null;
   },HttpContext.RequestAborted);
   if(error==null){TempData["ClientSettingsSaved"]=true;return RedirectToAction(nameof(Index),new{n});}ModelState.AddModelError("",error);
  }
  f.Name=await db.Partners.Where(p=>p.N==n).Select(p=>p.SiteName).SingleOrDefaultAsync()??"";return View(f);
 }
}
