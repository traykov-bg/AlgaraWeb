using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Algara.Data.Data;
using Algara.Data.Models;
using Algara.Data.Repositories;
using Algara.Identity.Authorization;
using Algara.Identity.Data;
using Algara.Web.Services.Cart;
using Algara.Web.Services.Email;
using Algara.Web.Services.Shipping;
using Algara.Web.ViewModels;
using Algara.Web.ViewModels.Admin;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
namespace Algara.Web.Services.Checkout;

public sealed class StaffOrderService(ShopDbContext db, DeliveryQuoteService delivery, CheckoutTokens tokens,
    EmailDeliveryReadiness readiness, EmailOutboxFactory outbox, TimeProvider clock, IHostEnvironment? environment = null)
{
    public static string Owner(ClaimsPrincipal actor) => "staff-order:" + CheckoutService.Owner(actor) + ":" + actor.FindFirstValue("SessionId") + ":" + actor.FindFirstValue("SecurityStamp");
    public static StaffOrderForm CopyForm(Order order,string submissionToken) => new()
    {
        CopiedFromOrderN=order.N,SubmissionToken=submissionToken,
        PartnerN=order.PartnerN??0,CustomerUserN=order.UserN,
        ContactName=order.ContactName??"",Phone=order.ContactPhone??"",Email=order.ContactEmail,
        Channel=order.CreationChannel=="InStore"?"InStore":"Phone",
        Reason=$"Копие на поръчка #{order.N}",Comment=order.Comment,InvoiceDetails=order.InvoiceDetails,
        Destination=OrderSplitService.ReadDestination(order.DeliverySnapshot),
        Lines=order.OrderItems.OrderBy(i=>i.N).GroupBy(i=>new {i.ProductN,i.VariantN})
            .Select(g=>new CartEntry(g.Key.ProductN,g.Key.VariantN,g.Sum(i=>i.Quantity))).ToList()
    };
    public static bool Valid(StaffOrderForm form)
    {
        if (string.IsNullOrWhiteSpace(form.Email)) form.Email=null;
        return Validator.TryValidateObject(form, new ValidationContext(form), null, true) &&
        Validator.TryValidateObject(form.Destination, new ValidationContext(form.Destination), null, true) &&
        form.Channel is "Phone" or "InStore" && Enum.IsDefined(form.Destination.Method) &&
        !string.IsNullOrWhiteSpace(form.ContactName) && !string.IsNullOrWhiteSpace(form.Phone) && !string.IsNullOrWhiteSpace(form.Reason) &&
        !form.ContactName.Any(char.IsControl) && !form.Phone.Any(char.IsControl) && !form.Reason.Any(char.IsControl) &&
        (string.IsNullOrWhiteSpace(form.Email) || AccountEmailRules.NormalizeEmail(form.Email) != null) &&
        form.Lines.Count is > 0 and <= CartService.MaxLines && AccountCartStore.ValidIdentities(form.Lines) &&
        form.Lines.All(l => l.Quantity is > 0 and <= CartService.MaxQuantity) &&
        form.Lines.Select(l => (l.ProductN,l.VariantN)).Distinct().Count() == form.Lines.Count;
    }

    public async Task<CartSnapshot> ReadCartAsync(IReadOnlyList<CartEntry> entries)
    {
        if (entries.Count > CartService.MaxLines || !AccountCartStore.ValidIdentities(entries)) return new();
        return await new CartService(new ProductRepository(db), new FormCart(entries), clock, new StockReservationReader(db,clock)).GetAsync();
    }
    private sealed class FormCart(IReadOnlyList<CartEntry> entries) : ICartStore
    {
        public IReadOnlyList<CartEntry> Read() => entries;
        public void Write(IReadOnlyList<CartEntry> value) => throw new NotSupportedException();
    }
    public static string Fingerprint(StaffOrderForm form, CartSnapshot cart, DeliveryQuote quote, string partnerSnapshot, string? linkVersion)
    {
        var destination = form.Destination;
        var priced = CheckoutTokens.Fingerprint(cart,new CheckoutForm { Method=destination.Method,City=destination.City,
            PostalCode=destination.PostalCode,Address=destination.Address,OfficeCode=destination.OfficeCode },quote);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {
            priced, form.Storefront,form.CopiedFromOrderN,form.Channel,form.PartnerN,form.CustomerUserN,form.ContactName,form.Email,form.Phone,form.Reason,
            form.Comment,form.InvoiceDetails,destination.AddressDetails,partnerSnapshot,linkVersion
        }))));
    }
    public async Task<int?> RunAsync(StaffOrderForm form, ClaimsPrincipal actor, bool submit, CancellationToken ct)
    {
        form.Error=null; form.Quote=null;
        try { return await RunCoreAsync(form,actor,submit,ct); }
        catch(DbUpdateException) { db.ChangeTracker.Clear();form.Error="Данните са променени. Изчислете отново.";form.PreviewToken=null;return null; }
        catch(SqlException ex) when(ex.Number is 1205 or 1222 or 51004) { db.ChangeTracker.Clear();form.Error="Поръчката се обработва. Опитайте отново.";form.PreviewToken=null;return null; }
    }
    private async Task<int?> RunCoreAsync(StaffOrderForm form, ClaimsPrincipal actor, bool submit, CancellationToken ct)
    {
        int? Fail(string message) { form.Error=message;form.PreviewToken=null;return null; }
        var ticket=tokens.Read(form.SubmissionToken,Owner(actor));
        if(ticket==null) return Fail("Формата е изтекла. Отворете нова служебна поръчка.");
        if(!Valid(form)) return Fail("Проверете клиента, контакта, основанието и артикулите.");
        if(submit && form.Destination.Method==DeliveryMethod.OwnTransport &&
            (string.IsNullOrWhiteSpace(form.Destination.Address) || form.Destination.Address.Any(char.IsControl))) return Fail("Въведете адрес за доставка.");
        await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
        await db.Database.ExecuteSqlRawAsync("""
            DECLARE @r int;
            EXEC @r=sys.sp_getapplock @Resource=N'Algara.AccessManagement',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=15000;
            IF @r<0 THROW 51004,'Order busy.',1;
            EXEC @r=sys.sp_getapplock @Resource=N'Algara.PartnerCatalog',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=15000;
            IF @r<0 THROW 51004,'Order busy.',1;
            EXEC @r=sys.sp_getapplock @Resource=N'Algara:ProductCodes',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=15000;
            IF @r<0 THROW 51004,'Order busy.',1;
            """,ct);
        await using var identity=new IdentityDbContext(new DbContextOptionsBuilder<IdentityDbContext>().UseSqlServer(db.Database.GetDbConnection()).Options);
        await identity.Database.UseTransactionAsync(tx.GetDbTransaction(),ct);
        var access=new StaffAccessService(identity);
        foreach(var permission in new[]{PermissionCatalog.Orders.View,PermissionCatalog.Orders.Create,PermissionCatalog.Partners.View})
            if(!await access.HasPermissionAsync(actor,permission,ct)) return Fail("Нямате право за служебно създаване на поръчки.");
        db.ChangeTracker.Clear();
        var existing=await db.Orders.AsNoTracking().SingleOrDefaultAsync(o=>o.SubmissionId==ticket.Id,ct);
        if(existing!=null) return existing.CreatedByStaffId==CheckoutService.Owner(actor)?existing.N:Fail("Невалидна поръчка.");
        if(form.CopiedFromOrderN.HasValue&&!await db.Orders.AnyAsync(o=>o.N==form.CopiedFromOrderN,ct))
            return Fail("Първоначалната поръчка не е намерена. Отворете нова служебна поръчка.");
        var partner=await db.Partners.AsNoTracking().Include(p=>p.Company).SingleOrDefaultAsync(p=>p.N==form.PartnerN&&p.IsActive,ct);
        if(partner==null) return Fail("Изберете активен контрагент.");
        var linkedIds=await db.PartnerAccountLinks.Where(l=>l.PartnerN==partner.N).Select(l=>l.UserN).ToListAsync(ct);
        var activeIds=await identity.Users.Where(u=>linkedIds.Contains(u.N)&&u.IsActive).Select(u=>u.N).ToListAsync(ct);
        if(activeIds.Count==1 && !form.CustomerUserN.HasValue) form.CustomerUserN=activeIds[0];
        if(activeIds.Count>0 && (!form.CustomerUserN.HasValue || !activeIds.Contains(form.CustomerUserN.Value)))
            return Fail("Изберете активен акаунт на контрагента. Поръчка без акаунт не е разрешена за този контрагент.");
        string? linkVersion=null;
        if(form.CustomerUserN.HasValue)
        {
            if(!await access.HasPermissionAsync(actor,PermissionCatalog.Users.View,ct) || !await access.HasPermissionAsync(actor,PermissionCatalog.Users.ViewDossier,ct)) return Fail("Нямате право за избор на клиентски акаунт.");
            var target=await identity.Users.AsNoTracking().SingleOrDefaultAsync(u=>u.N==form.CustomerUserN&&u.IsActive,ct);
            var link=await db.PartnerAccountLinks.AsNoTracking().SingleOrDefaultAsync(l=>l.UserN==form.CustomerUserN&&l.PartnerN==partner.N,ct);
            if(target==null || link==null || partner.Kind==PartnerKind.GeneralCustomer || !partner.AllowAccountLinks) return Fail("Акаунтът няма проверена връзка с този контрагент.");
            var actorRoles=await identity.UserRoles.Where(r=>r.User.Id==CheckoutService.Owner(actor)).Select(r=>r.Role.Name).ToListAsync(ct);
            var targetRoles=await identity.UserRoles.Where(r=>r.UserN==target.N).Select(r=>r.Role.Name).ToListAsync(ct);
            if(!StaffAccountManagementRules.CanManage(actorRoles,targetRoles)) return Fail("Нямате право да обслужвате този акаунт.");
            linkVersion=Convert.ToBase64String(link.RowVersion);
        }
        var partnerSnapshot=JsonSerializer.Serialize(new { partner.Code,partner.SiteName,partner.Kind,partner.CompanyN,
            CompanyName=partner.Company?.Name,LegalForm=partner.Company?.LegalForm,RegistrationNumber=partner.Company?.RegistrationNumber,VatNumber=partner.Company?.VatNumber,
            Version=Convert.ToBase64String(partner.RowVersion) }, new JsonSerializerOptions { Encoder=System.Text.Encodings.Web.JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All) });
        form.Cart=await ReadCartAsync(form.Lines);
        if(!form.Cart.CanQuote) return Fail("Проверете вариантите, цените и наличностите на артикулите.");
        var d=form.Destination;
        if(DeliveryQuoteService.IsOffice(d.Method)&&!string.IsNullOrWhiteSpace(d.City))
        {
            var offices=await delivery.OfficesAsync(form.Cart,d.Method,d.City,ct);
            if(offices.Success)d.Offices=offices.Offices;
        }
        var result=await delivery.CalculateAsync(form.Cart,d.Method,d.City,d.PostalCode,d.Address,d.OfficeCode,ct);
        if(result.Quote==null) return Fail("Доставката не може да бъде изчислена. Проверете транспорта, адреса и офиса.");
        form.Quote=result.Quote;
        if(result.Quote.IsTest && !OrderSplitService.AllowsSandbox(environment?.EnvironmentName)) return Fail("Тестова куриерска цена е разрешена само в среда Development.");
        var fingerprint=Fingerprint(form,form.Cart,result.Quote,partnerSnapshot,linkVersion);
        if(!submit) { form.PreviewToken=tokens.Quote(ticket,fingerprint);return null; }
        if(!form.CustomerAgreed || !tokens.Matches(form.PreviewToken,ticket,fingerprint)) return Fail("Изчислете отново и потвърдете съгласуваните с клиента данни и крайна сума.");
        var email=AccountEmailRules.NormalizeEmail(form.Email);
        var site=email==null?null:await readiness.GetPublicSiteUrlAsync(ct);
        if(email!=null&&site==null) return Fail("Настройте изпращането на имейли преди запис на поръчка с имейл.");
        AccountEmailRules.TryParse(ticket.GuestSecret,out _,out var secret);
        var now=clock.GetUtcNow().UtcDateTime;
        var order=new Order { PartnerN=partner.N,PartnerSnapshot=partnerSnapshot,UserN=form.CustomerUserN,
            CreatedByStaffId=CheckoutService.Owner(actor),CreationChannel=form.Channel,SubmissionId=ticket.Id,
            GuestTokenHash=email==null?null:Convert.ToHexString(SHA256.HashData(secret)),
            ContactName=form.ContactName.Trim(),ContactEmail=email,ContactPhone=form.Phone.Trim(),
            Comment=form.Comment?.Trim(),InvoiceDetails=form.InvoiceDetails?.Trim(),CreatedAt=now,ExpiresAtUtc=now.AddDays(7),
            DeliverySnapshot=JsonSerializer.Serialize(new { d.Method,Currency="EUR",Payment="OnReceipt",City=d.Method==DeliveryMethod.Pickup?null:d.City,PostalCode=d.Method==DeliveryMethod.Pickup?null:d.PostalCode,Address=d.Method==DeliveryMethod.Pickup||DeliveryQuoteService.IsOffice(d.Method)?null:d.Address,AddressDetails=d.Method==DeliveryMethod.Pickup||DeliveryQuoteService.IsOffice(d.Method)?null:d.AddressDetails,result.Quote.Office,result.Quote.ResolvedAddress,result.Quote.DistanceKm,result.Quote.IsTest }),DeliveryAmount=result.Quote.DeliveryAmount,TotalAmount=result.Quote.Total };
        foreach(var line in form.Cart.Lines)
        {
            var stock=line.Variant?.StockQuantity??line.Product!.StockQuantity??0;
            var reserved=await new StockReservationReader(db,clock).ReservedAsync(line.Entry.ProductN,line.Entry.VariantN);
            var quantity=Math.Min(line.Entry.Quantity,Math.Max(0,stock-reserved));
            order.OrderItems.Add(new OrderItem { ProductN=line.Entry.ProductN,VariantN=line.Entry.VariantN,Quantity=line.Entry.Quantity,
                UnitPrice=line.UnitPrice!.Value,ProductName=line.Name,VariantName=line.VariantName,Sku=line.Sku,
                ReservedQuantity=quantity,RequiresManufacturing=quantity<line.Entry.Quantity });
        }
        db.Orders.Add(order);
        db.Set<OrderEvent>().Add(new OrderEvent { Order=order,Action="StaffCreated",ActorId=order.CreatedByStaffId,
            Status=OrderStatus.Pending,OccurredAtUtc=now,Details=JsonSerializer.Serialize(new { form.Channel,form.Reason,form.CopiedFromOrderN,form.PartnerN,form.CustomerUserN,result.Quote.IsTest,CustomerAgreedAtUtc=now }) });
        // Only the first successful storefront submission consumes the staff member's cart.
        // Duplicate submissions return above, and failures roll this change back with the order.
        if(form.Storefront)await AccountCartStore.ClearAsync(db,order.CreatedByStaffId!,now,ct);
        await db.SaveChangesAsync(ct);
        if(email!=null)
        {
            identity.EmailOutboxMessages.Add(outbox.Create(email,(result.Quote.IsTest?"[TEST] ":"")+"Algara / Алгара — #"+order.N,
                (result.Quote.IsTest?"TEST — Тестова куриерска цена. / Test courier quote. ":"")+"Поръчката е записана от наш служител и очаква потвърждение. / Your order was recorded by our staff and awaits confirmation.",site+"/Checkout/Access#"+ticket.GuestSecret,null,null));
            await identity.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        return order.N;
    }
}
