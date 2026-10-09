using Algara.Data.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Algara.Web.Services;

public interface IProductGalleryWriteLock
{
    Task<IProductGalleryWriteLease> AcquireAsync(int productN, CancellationToken cancellationToken);
}

public interface IProductGalleryWriteLease : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken);
}

/// <summary>Serializes gallery mutations and form saves across application processes.</summary>
public sealed class ProductGalleryWriteLock(ShopDbContext db) : IProductGalleryWriteLock
{
    public async Task<IProductGalleryWriteLease> AcquireAsync(int productN, CancellationToken cancellationToken)
    {
        var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var resource = $"Algara.ProductGallery.{productN}";
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                DECLARE @result int;
                EXEC @result = sys.sp_getapplock @Resource={resource}, @LockMode='Exclusive',
                    @LockOwner='Transaction', @LockTimeout=15000;
                IF @result < 0 THROW 51001, 'The product gallery is busy. Retry the operation.', 1;
                """, cancellationToken);
            return new Lease(transaction);
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

    private sealed class Lease(IDbContextTransaction transaction) : IProductGalleryWriteLease
    {
        public Task CommitAsync(CancellationToken cancellationToken) => transaction.CommitAsync(cancellationToken);
        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
