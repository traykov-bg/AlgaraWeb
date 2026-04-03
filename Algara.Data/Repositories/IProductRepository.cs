using Algara.Data.Models;

namespace Algara.Data.Repositories
{
    public interface IProductRepository
    {
        Task<IEnumerable<Product>> GetAllAsync();
        Task<IEnumerable<Product>> GetFeaturedAsync(int count = 4);
        Task<IEnumerable<Product>> GetByCategoryAsync(int categoryN);
        Task<Product?> GetByNAsync(int n);
        Task AddAsync(Product product);
        Task UpdateAsync(Product product);
        Task DeleteAsync(int n);
    }
}
