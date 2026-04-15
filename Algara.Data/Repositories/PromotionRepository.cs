using Algara.Data.Data;
using Algara.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Algara.Data.Repositories
{
    public class PromotionRepository : IPromotionRepository
    {
        private readonly ShopDbContext _context;

        public PromotionRepository(ShopDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<Promotion>> GetAllAsync()
            => await _context.Promotions
                .Include(pr => pr.ProductPromotions)
                .OrderByDescending(pr => pr.CreatedAt)
                .ToListAsync();

        public async Task<Promotion?> GetByNAsync(int n)
            => await _context.Promotions.FirstOrDefaultAsync(pr => pr.N == n);

        public async Task<Promotion?> GetByNWithProductsAsync(int n)
            => await _context.Promotions
                .Include(pr => pr.ProductPromotions)
                    .ThenInclude(pp => pp.Product)
                .FirstOrDefaultAsync(pr => pr.N == n);

        public async Task AddAsync(Promotion promotion)
        {
            _context.Promotions.Add(promotion);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateAsync(Promotion promotion)
        {
            _context.Promotions.Update(promotion);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteAsync(int n)
        {
            var promotion = await _context.Promotions.FindAsync(n);
            if (promotion != null)
            {
                _context.Promotions.Remove(promotion);
                await _context.SaveChangesAsync();
            }
        }
    }
}
