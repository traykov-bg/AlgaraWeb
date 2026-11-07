using Algara.Identity.Data;
using Algara.Web.Services.Email;
using Microsoft.EntityFrameworkCore.Storage;
using Algara.Data.Data;
using Algara.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Algara.Web.Services.Checkout;

public sealed class OrderExpiryService(ShopDbContext db, TimeProvider clock, EmailOutboxFactory outbox)
{
    public async Task<int> ExpireAsync(CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await using var identity = new IdentityDbContext(new DbContextOptionsBuilder<IdentityDbContext>().UseSqlServer(db.Database.GetDbConnection()).Options);
        await identity.Database.UseTransactionAsync(tx.GetDbTransaction(), ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var expired = await db.Orders.FromSqlInterpolated($"""
            SELECT TOP(100) * FROM Orders WITH (UPDLOCK,READPAST,ROWLOCK)
            WHERE Status=0 AND ExpiresAtUtc <= {now} ORDER BY N
            """).ToListAsync(ct);
        foreach (var order in expired)
        {
            order.Status = OrderStatus.Cancelled;
            order.UpdatedAt = now;
            var message = OrderStatusEmail.Create(outbox, order, expired: true);
            if (message != null) identity.EmailOutboxMessages.Add(message);
            db.Set<OrderEvent>().Add(new OrderEvent { OrderN = order.N, Action = "Expired", ActorId = "system", Status = OrderStatus.Cancelled, OccurredAtUtc = now });
        }
        await db.SaveChangesAsync(ct);
        await identity.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return expired.Count;
    }
}
