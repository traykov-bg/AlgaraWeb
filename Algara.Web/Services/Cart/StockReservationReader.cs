using Algara.Data.Data;
using Algara.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Algara.Web.Services.Cart;

public interface IStockReservationReader
{
    Task<int> ReservedAsync(int productN, int? variantN);
}

public sealed class StockReservationReader(ShopDbContext db, TimeProvider clock) : IStockReservationReader
{
    public Task<int> ReservedAsync(int productN, int? variantN)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        return db.OrderItems.Where(i => i.ProductN == productN && i.VariantN == variantN &&
            (i.Order.Status == OrderStatus.Confirmed ||
             (i.Order.Status == OrderStatus.Pending && (i.Order.ExpiresAtUtc == null || i.Order.ExpiresAtUtc > now))))
            .SumAsync(i => i.ReservedQuantity > i.DeductedQuantity ? i.ReservedQuantity - i.DeductedQuantity : 0);
    }
}
