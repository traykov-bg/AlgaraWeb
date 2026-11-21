using Algara.Web.Services.Email;
using System.Data;
using System.Security.Claims;
using Algara.Identity.Data;
using Algara.Identity.Authorization;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Data.SqlClient;
using Algara.Data.Data;
using Algara.Data.Models;
using Algara.Web.Services.Cart;
using Microsoft.EntityFrameworkCore;

namespace Algara.Web.Services.Checkout;

public sealed class OrderLifecycleService(ShopDbContext db, TimeProvider clock, EmailOutboxFactory outbox)
{
    public static bool Allows(OrderStatus from, OrderStatus to) => (from, to) switch
    {
        (OrderStatus.Pending, OrderStatus.Confirmed or OrderStatus.Cancelled) => true,
        (OrderStatus.Confirmed, OrderStatus.Shipped or OrderStatus.Delivered or OrderStatus.Cancelled) => true,
        (OrderStatus.Shipped, OrderStatus.Delivered) => true,
        _ => false
    };

    public async Task<bool> ChangeAsync(int n, OrderStatus status, string? version, ClaimsPrincipal actor, CancellationToken ct, string? waybill = null, string? paymentException = null, DateOnly? deliveredOn = null)
    {
        try { return await ChangeCoreAsync(n, status, version, actor, ct, waybill, paymentException: paymentException, deliveredOn: deliveredOn); }
        catch (DbUpdateException) { db.ChangeTracker.Clear(); return false; }
        catch (SqlException ex) when (ex.Number is 1205 or 1222 or 51004) { db.ChangeTracker.Clear(); return false; }
    }

    public async Task<bool> ReadyAsync(int n,int itemN,int quantity,string version,string? note,ClaimsPrincipal actor,CancellationToken ct)
    {
        try { return await ChangeCoreAsync(n,OrderStatus.Confirmed,version,actor,ct,null,itemN,quantity,note); }
        catch(DbUpdateException) { db.ChangeTracker.Clear();return false; }
        catch(SqlException ex) when(ex.Number is 1205 or 1222 or 51004) { db.ChangeTracker.Clear();return false; }
    }
    private async Task<bool> ChangeCoreAsync(int n, OrderStatus status, string? version, ClaimsPrincipal actor, CancellationToken ct, string? waybill = null,int? readyItem=null,int readyQuantity=0,string? readyNote=null,string? paymentException=null,DateOnly? deliveredOn=null)
    {
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
        if (!await access.HasPermissionAsync(actor, PermissionCatalog.Orders.View, ct) ||
            !await access.HasPermissionAsync(actor, PermissionCatalog.Orders.ChangeStatus, ct)) return false;
        db.ChangeTracker.Clear();
        var order = await db.Orders.Include(o => o.OrderItems).SingleOrDefaultAsync(o => o.N == n, ct);
        if (order == null || Convert.ToBase64String(order.RowVersion) != version) return false;
        if(readyItem.HasValue) {
            var item=order.OrderItems.SingleOrDefault(i=>i.N==readyItem);
            if(order.Status!=OrderStatus.Confirmed || !string.IsNullOrEmpty(order.WaybillNumber) || item?.IndividualManufacture!=true || readyQuantity<0 || readyQuantity>item.Quantity || string.IsNullOrWhiteSpace(readyNote) || readyNote.Length>1000) return false;
            var previous=item.ReadyQuantity; item.ReadyQuantity=readyQuantity; order.UpdatedAt=clock.GetUtcNow().UtcDateTime;
            db.Add(new OrderEvent { OrderN=n,Action="ManufactureReady",ActorId=actor.FindFirstValue(ClaimTypes.NameIdentifier)!,Status=order.Status,OccurredAtUtc=order.UpdatedAt.Value,
                Details=System.Text.Json.JsonSerializer.Serialize(new { ItemN=item.N,Previous=previous,Ready=readyQuantity,Note=readyNote }) });
            await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return true;
        }
        waybill = string.IsNullOrWhiteSpace(waybill) ? null : waybill.Trim();
        if (waybill != null && (waybill.Length > 100 || waybill.Any(char.IsControl))) return false;
        var trackingOnly = order.Status == status && status is OrderStatus.Confirmed or OrderStatus.Shipped;
        if (!trackingOnly && !Allows(order.Status, status)) return false;
        if (waybill != null && waybill != order.WaybillNumber && status is not (OrderStatus.Confirmed or OrderStatus.Shipped)) return false;
        if (trackingOnly && (waybill == null || waybill == order.WaybillNumber)) return true;
        if (waybill != null) order.WaybillNumber = waybill;
        var now = clock.GetUtcNow().UtcDateTime;
        var paid=await db.OrderPayments.Where(p=>p.OrderN==n).SumAsync(p=>(decimal?)p.Amount,ct)??0m;
        if (order.Status == OrderStatus.Pending && order.ExpiresAtUtc <= now && paid<=0) return false;
        if(!trackingOnly && status is OrderStatus.Confirmed or OrderStatus.Delivered)
        {
            if(order.DepositPercent==null) await Algara.Web.Services.Payments.PaymentRules.InitializeAsync(db,order,ct);
            if(status==OrderStatus.Confirmed || !order.DeferredPaymentDays.HasValue) {
                var partner=await Algara.Web.Services.Payments.PaymentRules.PartnerAsync(db,order,ct);
                order.DepositExempt=partner?.DepositExempt??false;
                order.DeferredPaymentDays=partner?.DeferredPaymentDays??0;
                db.Add(new OrderEvent{OrderN=n,Action="PaymentTermsConfirmed",Status=status,ActorId=actor.FindFirstValue(ClaimTypes.NameIdentifier)!,OccurredAtUtc=now,Details=System.Text.Json.JsonSerializer.Serialize(new { PartnerN=partner?.N,PartnerCode=partner?.Code,PartnerName=partner?.SiteName,order.DepositPercent,order.DepositRequired,order.DepositExempt,order.DeferredPaymentDays })});
            }
            var insufficient=status==OrderStatus.Confirmed
                ? paid<Algara.Web.Services.Payments.PaymentRules.Deposit(order,order.DepositExempt==true)
                : paid<order.TotalAmount && order.DeferredPaymentDays.GetValueOrDefault()==0;
            if(insufficient) {
                if(string.IsNullOrWhiteSpace(paymentException) || paymentException.Length>1000 || !await access.HasPermissionAsync(actor,PermissionCatalog.Payments.Override,ct)) return false;
                db.Add(new OrderEvent{OrderN=n,Action="PaymentException",Status=status,ActorId=actor.FindFirstValue(ClaimTypes.NameIdentifier)!,OccurredAtUtc=now,Details=System.Text.Json.JsonSerializer.Serialize(new { Reason=paymentException.Trim(),Stage=status.ToString() })});
            }
            if(status==OrderStatus.Delivered) {
                var today=Algara.Web.Services.Payments.PaymentRules.Today(clock);
                if(!deliveredOn.HasValue || deliveredOn>today || deliveredOn<DateOnly.FromDateTime(order.CreatedAt)) return false;
                order.DeliveredOn=deliveredOn;
                order.PaymentDueOn=deliveredOn.Value.AddDays(order.DeferredPaymentDays.GetValueOrDefault());
            }
        }
        if (order.SubmissionId != null && !trackingOnly &&
            (status == OrderStatus.Confirmed || order.Status == OrderStatus.Confirmed))
        {
            foreach (var item in order.OrderItems.OrderBy(i => i.ProductN).ThenBy(i => i.VariantN))
            {
                if(item.IndividualManufacture) {
                    if(status is OrderStatus.Shipped or OrderStatus.Delivered && item.ReadyQuantity<item.Quantity) return false;
                    continue;
                }
                var remaining = item.Quantity - item.DeductedQuantity;
                if (status == OrderStatus.Cancelled && item.DeductedQuantity == 0) continue;
                if (status != OrderStatus.Cancelled && remaining == 0) continue;
                var product = await db.Products.SingleAsync(p => p.N == item.ProductN, ct);
                var variant = item.VariantN == null ? null : await db.ProductVariants.SingleOrDefaultAsync(v => v.N == item.VariantN && v.ProductN == item.ProductN, ct);
                if (item.VariantN != null && variant == null) return false;
                var stock = variant == null ? product.StockQuantity : variant.StockQuantity;
                int delta;
                if (status == OrderStatus.Cancelled)
                {
                    if (stock == null) return false;
                    delta = -item.DeductedQuantity;
                }
                else
                {
                    var ownReservation = Math.Max(0, item.ReservedQuantity - item.DeductedQuantity);
                    var otherReserved = await new StockReservationReader(db, clock).ReservedAsync(item.ProductN, item.VariantN) - ownReservation;
                    var available = Math.Max(0, (stock ?? 0) - otherReserved);
                    delta = Math.Min(remaining, available);
                    if (delta < remaining && (status != OrderStatus.Confirmed || !item.RequiresManufacturing)) return false;
                }
                if (delta == 0) continue;
                if (variant == null) product.StockQuantity = checked(stock!.Value - delta);
                else
                {
                    variant.StockQuantity = checked(stock!.Value - delta);
                    db.Entry(product).Property(p => p.StockQuantity).IsModified = true;
                }
                item.DeductedQuantity += delta;
                await db.SaveChangesAsync(ct);
            }
        }
        db.Set<OrderEvent>().Add(new OrderEvent { OrderN = order.N, Action = trackingOnly ? "WaybillUpdated" : "StatusChanged", ActorId = actor.FindFirstValue(ClaimTypes.NameIdentifier)!, Status = status, OccurredAtUtc = now });
        order.Status = status;
        order.UpdatedAt = now;
        if (status == OrderStatus.Confirmed) order.ConfirmedAtUtc = now;
        var message = trackingOnly ? null : OrderStatusEmail.Create(outbox, order);
        if (message != null) identity.EmailOutboxMessages.Add(message);
        await db.SaveChangesAsync(ct);
        await identity.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }
}
