using Algara.Data.Models;

namespace Algara.Data.Repositories
{
    public interface IPromotionRepository
    {
        Task<IEnumerable<Promotion>> GetAllAsync();
        Task<Promotion?> GetByNAsync(int n);
        Task<Promotion?> GetByNWithProductsAsync(int n);
        Task AddAsync(Promotion promotion);
        Task UpdateAsync(Promotion promotion);
        Task DeleteAsync(int n);
    }
}
