using Algara.Data.Data;
using Algara.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Algara.Data.Repositories
{
    public class SubCategoryRepository : ISubCategoryRepository
    {
        private readonly ShopDbContext _context;

        public SubCategoryRepository(ShopDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<SubCategory>> GetByCategoryAsync(int categoryN)
            => await _context.SubCategories
                .Where(sc => sc.CategoryN == categoryN && sc.IsActive)
                .OrderBy(sc => sc.Name)
                .ToListAsync();

        public async Task<SubCategory?> GetByNAsync(int n)
            => await _context.SubCategories.FindAsync(n);

        public async Task<SubCategory?> GetBySlugAsync(string slug)
            => await _context.SubCategories
                .FirstOrDefaultAsync(sc => sc.Slug == slug && sc.IsActive);

        public async Task AddAsync(SubCategory subCategory)
        {
            _context.SubCategories.Add(subCategory);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateAsync(SubCategory subCategory)
        {
            _context.SubCategories.Update(subCategory);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteAsync(int n)
        {
            var sub = await _context.SubCategories.FindAsync(n);
            if (sub != null)
            {
                sub.IsActive = false;
                await _context.SaveChangesAsync();
            }
        }
    }
}
