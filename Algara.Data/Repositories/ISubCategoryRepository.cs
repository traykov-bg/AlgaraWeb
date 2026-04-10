using Algara.Data.Models;

namespace Algara.Data.Repositories
{
    public interface ISubCategoryRepository
    {
        Task<IEnumerable<SubCategory>> GetByCategoryAsync(int categoryN);
        Task<SubCategory?> GetByNAsync(int n);
        Task<SubCategory?> GetBySlugAsync(string slug);
        Task AddAsync(SubCategory subCategory);
        Task UpdateAsync(SubCategory subCategory);
        Task DeleteAsync(int n);
    }
}
