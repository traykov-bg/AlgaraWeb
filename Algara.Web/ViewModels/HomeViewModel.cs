using Algara.Data.Models;

namespace Algara.Web.ViewModels
{
    public class HomeViewModel
    {
        public IEnumerable<Category> Categories { get; set; } = [];
        public IEnumerable<Product> FeaturedProducts { get; set; } = [];
        public IEnumerable<HeroSlide> HeroSlides { get; set; } = [];
    }
}
