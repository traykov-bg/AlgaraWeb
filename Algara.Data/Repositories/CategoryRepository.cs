using Algara.Data.Data;
using Algara.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Algara.Data.Repositories
{
    public class CategoryRepository : ICategoryRepository
    {
        private readonly ShopDbContext _context;

        public CategoryRepository(ShopDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<Category>> GetAllAsync()
            => await _context.Categories
                .Where(c => c.IsActive)
                .OrderBy(c => c.Name)
                .ToListAsync();

        public async Task<IEnumerable<Category>> GetFeaturedAsync()
            => await _context.Categories
                .Where(c => c.IsFeatured && c.IsActive)
                .OrderBy(c => c.Name)
                .ToListAsync();

        public async Task<Category?> GetByNAsync(int n)
            => await _context.Categories.FindAsync(n);

        public async Task AddAsync(Category category)
        {
            _context.Categories.Add(category);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateAsync(Category category)
        {
            _context.Categories.Update(category);
            await _context.SaveChangesAsync();
        }
    }
}
