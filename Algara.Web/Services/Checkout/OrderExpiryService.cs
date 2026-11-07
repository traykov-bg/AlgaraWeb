using Algara.Data.Data;
using Algara.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Algara.Web.Services.Checkout;

public sealed class OrderExpiryService(ShopDbContext db, TimeProvider clock)
{
    public async Task<int> ExpireAsync(CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var expired = await db.Orders.FromSqlInterpolated($"""
            SELECT TOP(100) * FROM Orders WITH (UPDLOCK,READPAST,ROWLOCK)
            WHERE Status=0 AND ExpiresAtUtc <= {now} ORDER BY N
            """).ToListAsync(ct);
        foreach (var order in expired)
        {
            order.Status = OrderStatus.Cancelled;
            order.UpdatedAt = now;
            db.Set<OrderEvent>().Add(new OrderEvent { OrderN = order.N, Action = "Expired", ActorId = "system", Status = OrderStatus.Cancelled, OccurredAtUtc = now });
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return expired.Count;
    }
}
