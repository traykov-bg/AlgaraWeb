using Algara.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Algara.Data.Repositories;

public partial class ProductRepository
{
    private async Task<IDbContextTransaction?> BeginCatalogWriteAsync()
    {
        var transaction = _context.Database.CurrentTransaction == null
            ? await _context.Database.BeginTransactionAsync() : null;
        try
        {
            // One lock covers both SKU namespaces, including concurrent manual edits.
            await _context.Database.ExecuteSqlRawAsync("""
                DECLARE @result int;
                EXEC @result = sys.sp_getapplock @Resource=N'Algara:ProductCodes',
                    @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=15000;
                IF @result < 0 THROW 51001, 'Could not acquire product code lock.', 1;
                """);
            return transaction;
        }
        catch
        {
            if (transaction != null) await transaction.DisposeAsync();
            throw;
        }
    }

    private async Task PrepareVariantCodesAsync(Product product)
    {
        var reserved = product.Variants.Where(v => !string.IsNullOrWhiteSpace(v.Sku))
            .Select(v => ProductSku.Normalize(v.Sku)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(product.Sku)) codes.Add(product.Sku);
        foreach (var variant in product.Variants)
        {
            variant.Sku = ProductSku.Normalize(variant.Sku);
            if (variant.Sku.Length == 0)
            {
                do
                {
                    var numbers = await _context.Database.SqlQueryRaw<long>(
                        "SELECT NEXT VALUE FOR [ProductVariantSkuNumbers] AS [Value]").ToListAsync();
                    variant.Sku = "V-" + ProductSku.FormatNumber(numbers.Single());
                } while (codes.Contains(variant.Sku) || reserved.Contains(variant.Sku) || await _context.Products.AnyAsync(p => p.Sku == variant.Sku) ||
                    await _context.ProductVariants.AnyAsync(v => v.Sku == variant.Sku));
            }
            if (!ProductSku.IsValid(variant.Sku) || !codes.Add(variant.Sku) ||
                await _context.Products.AnyAsync(p => p.Sku == variant.Sku) ||
                await _context.ProductVariants.AnyAsync(v => v.Sku == variant.Sku && v.N != variant.N))
                throw new ProductSkuConflictException(variant.Sku);
        }
    }
}