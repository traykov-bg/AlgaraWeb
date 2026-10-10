using Algara.Data.Data;
using Algara.Data.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Algara.Data.Repositories
{
    public partial class ProductRepository : IProductRepository
    {
        private readonly ShopDbContext _context;

        public ProductRepository(ShopDbContext context)
        {
            _context = context;
        }

        private static IQueryable<Product> WithPromotions(IQueryable<Product> q)
            => q.Include(p => p.ProductPromotions)
                    .ThenInclude(pp => pp.Promotion)
                .Include(p => p.Translations)
                .Include(p => p.Images)
                .Include(p => p.Variants).ThenInclude(v => v.Color).ThenInclude(c => c.Fabric)
                .Include(p => p.Variants).ThenInclude(v => v.Image)
                .Include(p => p.FabricSurcharges).ThenInclude(s => s.Fabric)
                .AsSplitQuery();

        public async Task<IEnumerable<Product>> GetAllAsync()
            => await WithPromotions(_context.Products.Include(p => p.Category))
                .Where(p => p.IsActive)
                .OrderBy(p => p.Name)
                .ToListAsync();

        public async Task<IEnumerable<Product>> GetFeaturedAsync(int count = 4)
            => await WithPromotions(_context.Products.Include(p => p.Category))
                .Where(p => p.IsFeatured && p.IsActive)
                .OrderByDescending(p => p.CreatedAt)
                .Take(count)
                .ToListAsync();

        public async Task<IEnumerable<Product>> GetByCategoryAsync(int categoryN)
            => await WithPromotions(_context.Products.Include(p => p.Category))
                .Where(p => p.CategoryN == categoryN && p.IsActive)
                .OrderBy(p => p.Name)
                .ToListAsync();

        public async Task<IEnumerable<Product>> GetBySubCategoryAsync(int subCategoryN)
            => await WithPromotions(_context.Products.Include(p => p.Category))
                .Where(p => p.IsActive && p.ProductSubCategories.Any(psc => psc.SubCategoryN == subCategoryN))
                .OrderBy(p => p.Name)
                .ToListAsync();

        public async Task<Product?> GetByNAsync(int n)
            => await WithPromotions(_context.Products.Include(p => p.Category))
                .FirstOrDefaultAsync(p => p.N == n);

        public async Task AddAsync(Product product)
        {
            await using var transaction = await BeginCatalogWriteAsync();
            product.Sku = ProductSku.Normalize(product.Sku);
            var automaticSku = product.Sku.Length == 0;
            if (!automaticSku)
                await ValidateSkuAsync(product);

            await PrepareVariantCodesAsync(product);
            _context.Products.Add(product);
            while (true)
            {
                if (automaticSku)
                {
                    do { product.Sku = await GetNextAvailableSkuAsync(); }
                    while (product.Variants.Any(v => v.Sku == product.Sku));
                }

                try
                {
                    await _context.SaveChangesAsync();
                    if (transaction != null) await transaction.CommitAsync();
                    return;
                }
                catch (DbUpdateException ex) when (IsSkuConflict(ex))
                {
                    // A manual edit can claim a reserved sequence number between
                    // the availability check and INSERT. Only automatic codes retry.
                    if (!automaticSku)
                        throw new ProductSkuConflictException(product.Sku, ex);
                }
            }
        }

        public async Task UpdateAsync(Product product)
        {
            await using var transaction = await BeginCatalogWriteAsync();
            product.Sku = ProductSku.Normalize(product.Sku);
            await ValidateSkuAsync(product);
            await PrepareVariantCodesAsync(product);
            if (_context.Entry(product).State == EntityState.Detached)
                _context.Products.Update(product);
            // Advance the product token even when only variant stock or gallery membership changes.
            _context.Entry(product).Property(p => p.Name).IsModified = true;
            try
            {
                await _context.SaveChangesAsync();
                if (transaction != null) await transaction.CommitAsync();
            }
            catch (DbUpdateException ex) when (IsSkuConflict(ex))
            {
                throw new ProductSkuConflictException(product.Sku, ex);
            }
        }

        private async Task ValidateSkuAsync(Product product)
        {
            if (!ProductSku.IsValid(product.Sku))
                throw new ArgumentException("SKU must contain 1 to 64 ASCII letters, digits, hyphens or underscores.", nameof(product));
            if (await _context.Products.AnyAsync(p => p.Sku == product.Sku && p.N != product.N) || await _context.ProductVariants.AnyAsync(v => v.Sku == product.Sku))
                throw new ProductSkuConflictException(product.Sku);
        }

        private async Task<string> GetNextAvailableSkuAsync()
        {
            while (true)
            {
                // Materialize before Single: SQL Server forbids NEXT VALUE FOR
                // inside the subquery that EF would otherwise add for composition.
                var numbers = await _context.Database.SqlQueryRaw<long>(
                    "SELECT NEXT VALUE FOR [ProductSkuNumbers] AS [Value]").ToListAsync();
                var sku = ProductSku.FormatNumber(numbers.Single());
                if (!await _context.Products.AnyAsync(p => p.Sku == sku) && !await _context.ProductVariants.AnyAsync(v => v.Sku == sku))
                    return sku;
            }
        }

        private static bool IsSkuConflict(DbUpdateException exception)
            => exception.InnerException is SqlException sql &&
               sql.Errors.Cast<SqlError>().Any(error => error.Number is 2601 or 2627 &&
                   error.Message.Contains("IX_Products_Sku", StringComparison.Ordinal));

        public async Task DeleteAsync(int n)
        {
            var product = await _context.Products.FindAsync(n);
            if (product != null)
            {
                product.IsActive = false; // мек delete — запазва исторически данни от поръчки
                await _context.SaveChangesAsync();
            }
        }
    }
}
