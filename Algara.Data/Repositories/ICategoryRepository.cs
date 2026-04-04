using Algara.Data.Models;

namespace Algara.Data.Repositories
{
    public interface ICategoryRepository
    {
        Task<IEnumerable<Category>> GetAllAsync();
        Task<IEnumerable<Category>> GetFeaturedAsync();
        Task<Category?> GetByNAsync(int n);
        Task<Category?> GetBySlugAsync(string slug);
        Task AddAsync(Category category);
        Task UpdateAsync(Category category);
        Task DeleteAsync(int n);
    }
}
