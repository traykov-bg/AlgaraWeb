using Algara.Data.Data;
using Algara.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Algara.Data.Repositories;

public class HeroSlideRepository : IHeroSlideRepository
{
    private readonly ShopDbContext _context;

    public HeroSlideRepository(ShopDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<HeroSlide>> GetActiveOrderedAsync()
        => await _context.HeroSlides
            .Where(s => s.IsActive)
            .OrderBy(s => s.DisplayOrder)
            .ThenBy(s => s.N)
            .ToListAsync();

    public async Task<IEnumerable<HeroSlide>> GetAllAsync()
        => await _context.HeroSlides
            .OrderBy(s => s.DisplayOrder)
            .ThenBy(s => s.N)
            .ToListAsync();

    public async Task<HeroSlide?> GetByNAsync(int n)
        => await _context.HeroSlides.FindAsync(n);

    public async Task AddAsync(HeroSlide slide)
    {
        _context.HeroSlides.Add(slide);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateAsync(HeroSlide slide)
    {
        _context.HeroSlides.Update(slide);
        await _context.SaveChangesAsync();
    }

    public async Task ToggleActiveAsync(int n)
    {
        var slide = await _context.HeroSlides.FindAsync(n);
        if (slide is null) return;
        slide.IsActive = !slide.IsActive;
        await _context.SaveChangesAsync();
    }
}
