using System.Data;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Algara.Data.Data;
using Algara.Data.Models;
using Algara.Identity.Authorization;
using Algara.Identity.Data;
using Algara.Web.Services.Cart;
using Algara.Web.Services.Email;
using Algara.Web.Services.Shipping;
using Algara.Web.ViewModels.Admin;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
namespace Algara.Web.Services.Checkout;

public sealed class OrderSplitService(ShopDbContext db, DeliveryQuoteService delivery, EmailDeliveryReadiness readiness,
 EmailOutboxFactory outbox, IDataProtectionProvider protection, TimeProvider clock, IHostEnvironment? environment = null)
{
 private readonly IDataProtector protector = protection.CreateProtector("Algara.OrderSplit.v1");
 private sealed record Ticket(string Fingerprint, DateTimeOffset Expires);
 public static bool AllowsSandbox(string? environmentName) => string.Equals(environmentName, Environments.Development, StringComparison.OrdinalIgnoreCase);
 public static bool CanSplit(Order order) => order.SubmissionId != null && order.Status == OrderStatus.Confirmed && string.IsNullOrEmpty(order.WaybillNumber);
 public static int ReadyQuantity(OrderItem item) => item.IndividualManufacture ? item.ReadyQuantity : Math.Max(item.ReservedQuantity, item.DeductedQuantity);
 public async Task<int?> RunAsync(int n, OrderSplitForm form, ClaimsPrincipal actor, bool submit, CancellationToken ct)
 {
  form.Error = null;
  try { return await RunCoreAsync(n, form, actor, submit, ct); }
  catch (DbUpdateException) { db.ChangeTracker.Clear(); form.Error = "Поръчката е променена. Презаредете и изчислете отново."; return null; }
  catch (SqlException ex) when (ex.Number is 1205 or 1222 or 51004) { db.ChangeTracker.Clear(); form.Error = "Поръчката се обработва. Опитайте отново."; return null; }
 }
 private async Task<int?> RunCoreAsync(int n, OrderSplitForm form, ClaimsPrincipal actor, bool submit, CancellationToken ct)
 {
  int? Fail(string text) { form.Error = text; form.PreviewToken = null; return null; }
  if (submit && new[] { form.Remaining, form.Separated }.Any(d => d.Method == DeliveryMethod.OwnTransport && (string.IsNullOrWhiteSpace(d.Address) || d.Address.Any(char.IsControl)))) return Fail("Въведете адрес за всяка доставка със собствен транспорт.");
  string? site = null;
  await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
  await db.Database.ExecuteSqlRawAsync("""
   DECLARE @r int;
   EXEC @r=sys.sp_getapplock @Resource=N'Algara.AccessManagement',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=15000;
   IF @r<0 THROW 51004,'Order busy.',1;
   EXEC @r=sys.sp_getapplock @Resource=N'Algara:ProductCodes',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=15000;
   IF @r<0 THROW 51004,'Order busy.',1;
   """, ct);
  await using var identity = new IdentityDbContext(new DbContextOptionsBuilder<IdentityDbContext>().UseSqlServer(db.Database.GetDbConnection()).Options);
  await identity.Database.UseTransactionAsync(tx.GetDbTransaction(), ct);
  var access = new StaffAccessService(identity);
  if (!await access.HasPermissionAsync(actor, PermissionCatalog.Orders.View, ct) || !await access.HasPermissionAsync(actor, PermissionCatalog.Orders.Split, ct)) return Fail("Нямате право за разделяне на поръчки.");
  db.ChangeTracker.Clear();
  var order = await db.Orders.Include(o=>o.OrderItems).ThenInclude(i=>i.Product).ThenInclude(p=>p.ShippingParcels).SingleOrDefaultAsync(o=>o.N==n, ct);
  if (order == null) return Fail("Поръчката не е намерена.");
  form.Order = order;
  var paid=await db.OrderPayments.Where(p=>p.OrderN==n).SumAsync(p=>(decimal?)p.Amount,ct)??0m;
  form.Paid=paid;
  if(paid>0 && (!await access.HasPermissionAsync(actor,PermissionCatalog.Payments.View,ct) || !await access.HasPermissionAsync(actor,PermissionCatalog.Payments.Record,ct))) return Fail("Разделяне на платена поръчка изисква преглед и отчитане на плащания.");
  if(!Algara.Web.Services.Payments.PaymentService.Parse(form.PaymentAmount,out var paymentAmount) || paymentAmount<0 || decimal.Round(paymentAmount,2)!=paymentAmount || paymentAmount>paid) return Fail("Проверете сумата за прехвърляне към отделената поръчка.");
  var email = AccountEmailRules.NormalizeEmail(order.ContactEmail);
  if (submit && email != null) { site = await readiness.GetPublicSiteUrlAsync(ct); if (site == null) return Fail("Настройте изпращането на имейли преди разделяне."); }
  if (!CanSplit(order) || Convert.ToBase64String(order.RowVersion)!=form.Version) return Fail("Разделяне е възможно само за непроменена потвърдена поръчка без товарителница.");
  if (string.IsNullOrWhiteSpace(form.Reason) || form.Reason.Length > 500 || form.Reason.Any(char.IsControl)) return Fail("Посочете кратко основание за разделянето.");
  if (form.Quantities.Keys.Any(id=>order.OrderItems.All(i=>i.N!=id))) return Fail("Невалидни артикули.");
  var allocation=AgreedOffer.SplitServices(AgreedOffer.Services(order.ServicesJson),form.ServiceAmounts);
  if(allocation==null)return Fail("Проверете разпределението на услугите; общата стойност трябва да се запази.");
  var moved = new List<CartLine>(); var retained = new List<CartLine>();
  foreach(var item in order.OrderItems.OrderBy(i=>i.N))
  {
   var quantity=form.Quantities.GetValueOrDefault(item.N);
   if(quantity<0 || quantity>item.Quantity || quantity>ReadyQuantity(item)) return Fail("Изберете само наличната част за отделяне и оставете артикули в първоначалната поръчка.");
   if(quantity>0) moved.Add(new CartLine{Entry=new(item.ProductN,item.VariantN,quantity),Product=item.Product,UnitPrice=item.UnitPrice,IndividualManufacture=item.IndividualManufacture});
   if(quantity<item.Quantity) retained.Add(new CartLine{Entry=new(item.ProductN,item.VariantN,item.Quantity-quantity),Product=item.Product,UnitPrice=item.UnitPrice});
  }
  if(moved.Count==0 || retained.Count==0) return Fail("И двете поръчки трябва да съдържат поне един артикул.");
  var leftCart=new CartSnapshot{Lines=retained};var rightCart=new CartSnapshot{Lines=moved};
  async Task<DeliveryQuoteResult> Quote(CartSnapshot cart, SplitDestination destination)
  {
   if(DeliveryQuoteService.IsOffice(destination.Method) && !string.IsNullOrWhiteSpace(destination.City))
   {
    var offices=await delivery.OfficesAsync(cart,destination.Method,destination.City,ct);
    if(offices.Success) destination.Offices=offices.Offices;
   }
   return await delivery.CalculateAsync(cart,destination.Method,destination.City,destination.PostalCode,destination.Address,destination.OfficeCode,ct);
  }
  var left=await Quote(leftCart,form.Remaining); var right=await Quote(rightCart,form.Separated);
  if(left.Quote==null || right.Quote==null) return Fail("Доставката не може да бъде изчислена. Проверете допустимия транспорт, адресите и избраните офиси. ");
  left=new(left.Quote with { ServicesAmount=allocation.Value.Remaining.Sum(s=>s.Amount) });
  right=new(right.Quote with { ServicesAmount=allocation.Value.Separated.Sum(s=>s.Amount) });
  var testQuote = left.Quote!.IsTest || right.Quote!.IsTest;
  if(testQuote && !AllowsSandbox(environment?.EnvironmentName)) return Fail("Тестова куриерска калкулация може да се използва за разделяне само в среда Development.");
  form.RemainingQuote=left.Quote;form.SeparatedQuote=right.Quote;
  var fingerprint=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {
   n,form.Version,Actor=actor.FindFirstValue(ClaimTypes.NameIdentifier),Session=actor.FindFirstValue("SessionId"),Stamp=actor.FindFirstValue("SecurityStamp"),form.Reason,
   PaymentAmount=paymentAmount,Paid=paid,form.PaymentException,
   Quantities=form.Quantities.OrderBy(p=>p.Key),Services=form.ServiceAmounts.OrderBy(p=>p.Key),
   Left=DestinationSnapshot(form.Remaining,left.Quote),Right=DestinationSnapshot(form.Separated,right.Quote!),
   LeftTotal=left.Quote.Total,RightTotal=right.Quote!.Total
  }))));
  if(!submit){form.PreviewToken=protector.Protect(JsonSerializer.Serialize(new Ticket(fingerprint,clock.GetUtcNow().AddMinutes(15))));return null;}
  Ticket? ticket=null;
  try { if(form.PreviewToken?.Length is >0 and <20000) ticket=JsonSerializer.Deserialize<Ticket>(protector.Unprotect(form.PreviewToken)); }
  catch(Exception ex) when(ex is CryptographicException or JsonException) { }
  if(!form.CustomerAgreed || ticket?.Fingerprint!=fingerprint || ticket.Expires<=clock.GetUtcNow()) return Fail("Изчислете отново и потвърдете, че клиентът е приел двете нови суми.");
  var guest=AccountEmailRules.Issue();var now=clock.GetUtcNow().UtcDateTime;
  var child=new Order{InquiryN=order.InquiryN,InquiryOfferN=order.InquiryOfferN,ServicesJson=JsonSerializer.Serialize(allocation.Value.Separated),SplitFromN=order.N,UserN=order.UserN,PartnerN=order.PartnerN,PartnerSnapshot=order.PartnerSnapshot,CreationChannel=order.CreationChannel,CreatedByStaffId=order.CreatedByStaffId,SubmissionId=guest.Id,GuestTokenHash=guest.Hash,ContactName=order.ContactName,
   ContactEmail=order.ContactEmail,ContactPhone=order.ContactPhone,Comment=order.Comment,InvoiceDetails=order.InvoiceDetails,
   Status=OrderStatus.Confirmed,CreatedAt=now,ConfirmedAtUtc=order.ConfirmedAtUtc,TermsAcceptedAtUtc=order.TermsAcceptedAtUtc,
   DeliverySnapshot=DestinationSnapshot(form.Separated,right.Quote),DeliveryAmount=right.Quote.DeliveryAmount,TotalAmount=right.Quote.Total};
  child.DepositPercent=order.DepositPercent;child.DepositExempt=order.DepositExempt;child.DeferredPaymentDays=order.DeferredPaymentDays;
  if(paymentAmount>child.TotalAmount || paid-paymentAmount>left.Quote.Total) return Fail("Разпределеното плащане надвишава стойността на една от поръчките.");
  var childNeedsDeposit=form.Separated.Method==DeliveryMethod.OwnTransport || moved.Any(l=>l.IndividualManufacture);
  var parentNeedsDeposit=form.Remaining.Method==DeliveryMethod.OwnTransport || order.OrderItems.Any(i=>i.Quantity>form.Quantities.GetValueOrDefault(i.N) && (i.IndividualManufacture || i.RequiresManufacturing));
  var insufficient=order.DepositExempt!=true && ((childNeedsDeposit && paymentAmount<Math.Ceiling(child.TotalAmount*(order.DepositPercent??50m))/100m) || (parentNeedsDeposit && paid-paymentAmount<Math.Ceiling(left.Quote.Total*(order.DepositPercent??50m))/100m));
  if(insufficient && (string.IsNullOrWhiteSpace(form.PaymentException) || form.PaymentException.Length>1000 || !await access.HasPermissionAsync(actor,PermissionCatalog.Payments.Override,ct))) return Fail("Недостатъчно капаро след разделянето. Нужно е право за изключение и причина.");
  var beforeTotal=order.TotalAmount;
  foreach(var item in order.OrderItems.ToArray())
  {
   var quantity=form.Quantities.GetValueOrDefault(item.N);if(quantity==0)continue;
   child.OrderItems.Add(OrderSplitQuantities.Transfer(item,quantity));
   if(item.Quantity==0){db.OrderItems.Remove(item);order.OrderItems.Remove(item);}
  }
  order.ServicesJson=JsonSerializer.Serialize(allocation.Value.Remaining);
  order.DeliverySnapshot=DestinationSnapshot(form.Remaining,left.Quote);order.DeliveryAmount=left.Quote.DeliveryAmount;order.TotalAmount=left.Quote.Total;order.UpdatedAt=now;
  child.DepositRequired=Algara.Web.Services.Payments.PaymentRules.RequiresDeposit(child);
  order.DepositRequired=Algara.Web.Services.Payments.PaymentRules.RequiresDeposit(order);
  db.Orders.Add(child);await db.SaveChangesAsync(ct);
  if(paymentAmount>0) {
   foreach(var target in new[]{order,child}) db.OrderPayments.Add(new OrderPayment{OrderN=target.N,SubmissionId=Guid.NewGuid(),Kind=PaymentEntryKind.Allocation,Amount=target==child?paymentAmount:-paymentAmount,RelatedOrderN=target==child?order.N:child.N,Location="Разделяне / Split",MethodName="Разпределение / Allocation",PaidOn=Algara.Web.Services.Payments.PaymentRules.Today(clock),RecordedAtUtc=now,ActorId=actor.FindFirstValue(ClaimTypes.NameIdentifier)!,ActorName=actor.Identity?.Name??"",Note=form.Reason});
  }
  var details=JsonSerializer.Serialize(new { Parent=order.N,Child=child.N,BeforeTotal=beforeTotal,RemainingTotal=order.TotalAmount,SeparatedTotal=child.TotalAmount,IsTest=testQuote,Services=form.ServiceAmounts,PaymentAllocation=paymentAmount,PaymentException=insufficient?form.PaymentException:null,form.Reason });
  foreach(var target in new[]{order,child}) db.Set<OrderEvent>().Add(new OrderEvent{OrderN=target.N,Action="Split",Status=target.Status,ActorId=actor.FindFirstValue(ClaimTypes.NameIdentifier)!,OccurredAtUtc=now,Details=details});
  if(email != null) identity.EmailOutboxMessages.Add(outbox.Create(email,(testQuote ? "[TEST] " : "")+"Algara / Алгара — разделяне на поръчка #"+order.N,
   (testQuote ? "Тестова куриерска калкулация. / Test courier quote. " : "") + $"По съгласуване с Вас поръчка #{order.N} е разделена. Оставаща поръчка: {order.TotalAmount:N2} EUR. Нова поръчка #{child.N}: {child.TotalAmount:N2} EUR. / Your order was split as agreed. Remaining order: {order.TotalAmount:N2} EUR. New order #{child.N}: {child.TotalAmount:N2} EUR.",site+"/Checkout/Access#"+guest.LinkValue,null,null));
  await db.SaveChangesAsync(ct);await identity.SaveChangesAsync(ct);await tx.CommitAsync(ct);return child.N;
 }
 public static string DestinationSnapshot(SplitDestination destination,DeliveryQuote quote)=>JsonSerializer.Serialize(new{
  destination.Method,Currency="EUR",Payment="OnReceipt",TermsPolicyVersion="1.0",
  City=destination.Method==DeliveryMethod.Pickup?null:destination.City,
  PostalCode=destination.Method==DeliveryMethod.Pickup||DeliveryQuoteService.IsOffice(destination.Method)?null:destination.PostalCode,
  Address=destination.Method==DeliveryMethod.Pickup||DeliveryQuoteService.IsOffice(destination.Method)?null:destination.Address,
  AddressDetails=destination.Method==DeliveryMethod.Pickup||DeliveryQuoteService.IsOffice(destination.Method)?null:destination.AddressDetails,
  quote.Office,quote.ResolvedAddress,quote.DistanceKm,quote.IsTest
 });
 public static SplitDestination ReadDestination(string? snapshot)
 {
  if(snapshot==null)return new();
  using var doc=JsonDocument.Parse(snapshot);var root=doc.RootElement;
  string? Text(string key)=>root.TryGetProperty(key,out var value)&&value.ValueKind==JsonValueKind.String?value.GetString():null;
  return new(){Method=(DeliveryMethod)root.GetProperty("Method").GetInt32(),City=Text("City"),PostalCode=Text("PostalCode"),Address=Text("Address"),AddressDetails=Text("AddressDetails"),
   OfficeCode=root.TryGetProperty("Office",out var office)&&office.ValueKind==JsonValueKind.Object?office.GetProperty("Code").GetString():null};
 }
}
