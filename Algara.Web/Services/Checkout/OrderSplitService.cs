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
 public static int ReadyQuantity(OrderItem item) => Math.Max(item.ReservedQuantity, item.DeductedQuantity);
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
  var site = submit ? await readiness.GetPublicSiteUrlAsync(ct) : null;
  if (submit && site == null) return Fail("Настройте изпращането на имейли преди разделяне.");
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
  if (!CanSplit(order) || Convert.ToBase64String(order.RowVersion)!=form.Version) return Fail("Разделяне е възможно само за непроменена потвърдена поръчка без товарителница.");
  if (string.IsNullOrWhiteSpace(form.Reason) || form.Reason.Length > 500 || form.Reason.Any(char.IsControl)) return Fail("Посочете кратко основание за разделянето.");
  if (form.Quantities.Keys.Any(id=>order.OrderItems.All(i=>i.N!=id))) return Fail("Невалидни артикули.");
  var moved = new List<CartLine>(); var retained = new List<CartLine>();
  foreach(var item in order.OrderItems.OrderBy(i=>i.N))
  {
   var quantity=form.Quantities.GetValueOrDefault(item.N);
   if(quantity<0 || quantity>item.Quantity || quantity>ReadyQuantity(item)) return Fail("Изберете само наличната част за отделяне и оставете артикули в първоначалната поръчка.");
   if(quantity>0) moved.Add(new CartLine{Entry=new(item.ProductN,item.VariantN,quantity),Product=item.Product,UnitPrice=item.UnitPrice});
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
  var testQuote = left.Quote.IsTest || right.Quote.IsTest;
  if(testQuote && !AllowsSandbox(environment?.EnvironmentName)) return Fail("Тестова куриерска калкулация може да се използва за разделяне само в среда Development.");
  form.RemainingQuote=left.Quote;form.SeparatedQuote=right.Quote;
  var fingerprint=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {
   n,form.Version,Actor=actor.FindFirstValue(ClaimTypes.NameIdentifier),Session=actor.FindFirstValue("SessionId"),Stamp=actor.FindFirstValue("SecurityStamp"),form.Reason,
   Quantities=form.Quantities.OrderBy(p=>p.Key),
   Left=DestinationSnapshot(form.Remaining,left.Quote),Right=DestinationSnapshot(form.Separated,right.Quote),
   LeftTotal=left.Quote.Total,RightTotal=right.Quote.Total
  }))));
  if(!submit){form.PreviewToken=protector.Protect(JsonSerializer.Serialize(new Ticket(fingerprint,clock.GetUtcNow().AddMinutes(15))));return null;}
  Ticket? ticket=null;
  try { if(form.PreviewToken?.Length is >0 and <20000) ticket=JsonSerializer.Deserialize<Ticket>(protector.Unprotect(form.PreviewToken)); }
  catch(Exception ex) when(ex is CryptographicException or JsonException) { }
  if(!form.CustomerAgreed || ticket?.Fingerprint!=fingerprint || ticket.Expires<=clock.GetUtcNow()) return Fail("Изчислете отново и потвърдете, че клиентът е приел двете нови суми.");
  var guest=AccountEmailRules.Issue();var now=clock.GetUtcNow().UtcDateTime;
  var child=new Order{SplitFromN=order.N,UserN=order.UserN,SubmissionId=guest.Id,GuestTokenHash=guest.Hash,ContactName=order.ContactName,
   ContactEmail=order.ContactEmail,ContactPhone=order.ContactPhone,Comment=order.Comment,InvoiceDetails=order.InvoiceDetails,
   Status=OrderStatus.Confirmed,CreatedAt=now,ConfirmedAtUtc=order.ConfirmedAtUtc,TermsAcceptedAtUtc=order.TermsAcceptedAtUtc,
   DeliverySnapshot=DestinationSnapshot(form.Separated,right.Quote),DeliveryAmount=right.Quote.DeliveryAmount,TotalAmount=right.Quote.Total};
  var beforeTotal=order.TotalAmount;
  foreach(var item in order.OrderItems.ToArray())
  {
   var quantity=form.Quantities.GetValueOrDefault(item.N);if(quantity==0)continue;
   child.OrderItems.Add(OrderSplitQuantities.Transfer(item,quantity));
   if(item.Quantity==0){db.OrderItems.Remove(item);order.OrderItems.Remove(item);}
  }
  order.DeliverySnapshot=DestinationSnapshot(form.Remaining,left.Quote);order.DeliveryAmount=left.Quote.DeliveryAmount;order.TotalAmount=left.Quote.Total;order.UpdatedAt=now;
  db.Orders.Add(child);await db.SaveChangesAsync(ct);
  var details=JsonSerializer.Serialize(new { Parent=order.N,Child=child.N,BeforeTotal=beforeTotal,RemainingTotal=order.TotalAmount,SeparatedTotal=child.TotalAmount,IsTest=testQuote,form.Reason });
  foreach(var target in new[]{order,child}) db.Set<OrderEvent>().Add(new OrderEvent{OrderN=target.N,Action="Split",Status=target.Status,ActorId=actor.FindFirstValue(ClaimTypes.NameIdentifier)!,OccurredAtUtc=now,Details=details});
  identity.EmailOutboxMessages.Add(outbox.Create(order.ContactEmail!,(testQuote ? "[TEST] " : "")+"Algara / Алгара — разделяне на поръчка #"+order.N,
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
