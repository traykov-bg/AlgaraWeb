using Algara.Data.Models;

namespace Algara.Data.Repositories
{
    public interface ICategoryRepository
    {
        Task<IEnumerable<Category>> GetAllAsync();
        Task<Category?> GetByNAsync(int n);
        Task AddAsync(Category category);
        Task UpdateAsync(Category category);
    }
}
