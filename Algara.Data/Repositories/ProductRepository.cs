using Algara.Data.Data;
using Algara.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Algara.Data.Repositories
{
    public class ProductRepository : IProductRepository
    {
        private readonly ShopDbContext _context;

        public ProductRepository(ShopDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<Product>> GetAllAsync()
            => await _context.Products
                .Include(p => p.Category)
                .Where(p => p.IsActive)
                .OrderBy(p => p.Name)
                .ToListAsync();

        public async Task<IEnumerable<Product>> GetFeaturedAsync(int count = 4)
            => await _context.Products
                .Include(p => p.Category)
                .Where(p => p.IsFeatured && p.IsActive)
                .OrderByDescending(p => p.CreatedAt)
                .Take(count)
                .ToListAsync();

        public async Task<IEnumerable<Product>> GetByCategoryAsync(int categoryN)
            => await _context.Products
                .Include(p => p.Category)
                .Where(p => p.CategoryN == categoryN && p.IsActive)
                .OrderBy(p => p.Name)
                .ToListAsync();

        public async Task<Product?> GetByNAsync(int n)
            => await _context.Products
                .Include(p => p.Category)
                .FirstOrDefaultAsync(p => p.N == n);

        public async Task AddAsync(Product product)
        {
            _context.Products.Add(product);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateAsync(Product product)
        {
            _context.Products.Update(product);
            await _context.SaveChangesAsync();
        }

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
