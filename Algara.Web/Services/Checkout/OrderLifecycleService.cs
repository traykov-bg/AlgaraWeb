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

public sealed class OrderLifecycleService(ShopDbContext db, TimeProvider clock)
{
    public static bool Allows(OrderStatus from, OrderStatus to) => (from, to) switch
    {
        (OrderStatus.Pending, OrderStatus.Confirmed or OrderStatus.Cancelled) => true,
        (OrderStatus.Confirmed, OrderStatus.Shipped or OrderStatus.Delivered or OrderStatus.Cancelled) => true,
        (OrderStatus.Shipped, OrderStatus.Delivered) => true,
        _ => false
    };

    public async Task<bool> ChangeAsync(int n, OrderStatus status, string? version, ClaimsPrincipal actor, CancellationToken ct)
    {
        try { return await ChangeCoreAsync(n, status, version, actor, ct); }
        catch (DbUpdateException) { db.ChangeTracker.Clear(); return false; }
        catch (SqlException ex) when (ex.Number is 1205 or 1222 or 51004) { db.ChangeTracker.Clear(); return false; }
    }

    private async Task<bool> ChangeCoreAsync(int n, OrderStatus status, string? version, ClaimsPrincipal actor, CancellationToken ct)
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
        if (order == null || Convert.ToBase64String(order.RowVersion) != version || !Allows(order.Status, status)) return false;
        var now = clock.GetUtcNow().UtcDateTime;
        if (order.Status == OrderStatus.Pending && order.ExpiresAtUtc <= now) return false;
        if (order.SubmissionId != null && order.Status == OrderStatus.Confirmed && status is OrderStatus.Shipped or OrderStatus.Delivered)
        {
            foreach (var item in order.OrderItems.OrderBy(i => i.ProductN).ThenBy(i => i.VariantN))
            {
                var product = await db.Products.SingleAsync(p => p.N == item.ProductN, ct);
                var variant = item.VariantN == null ? null : await db.ProductVariants.SingleOrDefaultAsync(v => v.N == item.VariantN && v.ProductN == item.ProductN, ct);
                if (item.VariantN != null && variant == null) return false;
                var stock = variant == null ? product.StockQuantity : variant.StockQuantity;
                var otherReserved = await new StockReservationReader(db, clock).ReservedAsync(item.ProductN, item.VariantN) - item.ReservedQuantity;
                if (stock == null || stock - otherReserved < item.Quantity) return false;
                if (variant == null) product.StockQuantity -= item.Quantity;
                else
                {
                    variant.StockQuantity -= item.Quantity;
                    // Catalog edits use the parent row version for all variant fields.
                    // Touch it so an editor opened before shipment cannot restore old stock.
                    db.Entry(product).Property(p => p.StockQuantity).IsModified = true;
                }
            }
        }
        db.Set<OrderEvent>().Add(new OrderEvent { OrderN = order.N, Action = "StatusChanged", ActorId = actor.FindFirstValue(ClaimTypes.NameIdentifier)!, Status = status, OccurredAtUtc = now });
        order.Status = status;
        order.UpdatedAt = now;
        if (status == OrderStatus.Confirmed) order.ConfirmedAtUtc = now;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }
}
