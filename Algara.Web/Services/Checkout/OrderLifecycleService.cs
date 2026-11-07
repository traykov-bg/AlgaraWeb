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

    public async Task<bool> ChangeAsync(int n, OrderStatus status, string? version, ClaimsPrincipal actor, CancellationToken ct, string? waybill = null)
    {
        try { return await ChangeCoreAsync(n, status, version, actor, ct, waybill); }
        catch (DbUpdateException) { db.ChangeTracker.Clear(); return false; }
        catch (SqlException ex) when (ex.Number is 1205 or 1222 or 51004) { db.ChangeTracker.Clear(); return false; }
    }

    private async Task<bool> ChangeCoreAsync(int n, OrderStatus status, string? version, ClaimsPrincipal actor, CancellationToken ct, string? waybill = null)
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
        waybill = string.IsNullOrWhiteSpace(waybill) ? null : waybill.Trim();
        if (waybill != null && (waybill.Length > 100 || waybill.Any(char.IsControl))) return false;
        var trackingOnly = order.Status == status && status is OrderStatus.Confirmed or OrderStatus.Shipped;
        if (!trackingOnly && !Allows(order.Status, status)) return false;
        if (waybill != null && waybill != order.WaybillNumber && status is not (OrderStatus.Confirmed or OrderStatus.Shipped)) return false;
        if (trackingOnly && (waybill == null || waybill == order.WaybillNumber)) return true;
        if (waybill != null) order.WaybillNumber = waybill;
        var now = clock.GetUtcNow().UtcDateTime;
        if (order.Status == OrderStatus.Pending && order.ExpiresAtUtc <= now) return false;
        if (order.SubmissionId != null && !trackingOnly &&
            (status == OrderStatus.Confirmed || order.Status == OrderStatus.Confirmed))
        {
            foreach (var item in order.OrderItems.OrderBy(i => i.ProductN).ThenBy(i => i.VariantN))
            {
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
