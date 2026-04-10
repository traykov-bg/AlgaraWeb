using System.ComponentModel.DataAnnotations;

namespace Algara.Data.Models
{
    public class SubCategory
    {
        public int N { get; set; }
        public int CategoryN { get; set; }

        [MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(200)]
        public string Slug { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public Category Category { get; set; } = null!;
        public ICollection<ProductSubCategory> ProductSubCategories { get; set; } = new List<ProductSubCategory>();
    }
}
