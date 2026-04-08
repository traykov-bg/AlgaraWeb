using Algara.Data.Models;

namespace Algara.Data.Repositories;

public interface IHeroSlideRepository
{
    Task<IEnumerable<HeroSlide>> GetActiveOrderedAsync();
    Task<IEnumerable<HeroSlide>> GetAllAsync();
    Task<HeroSlide?> GetByNAsync(int n);
    Task AddAsync(HeroSlide slide);
    Task UpdateAsync(HeroSlide slide);
    Task ToggleActiveAsync(int n);
}
