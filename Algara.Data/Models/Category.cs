using System.ComponentModel.DataAnnotations;

namespace Algara.Data.Models
{
    public class Category
    {
        public int N { get; set; }

        [MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        /// <summary>URL-friendly slug, e.g. "meka-mebel". Used in public routes instead of PK.</summary>
        [MaxLength(200)]
        public string Slug { get; set; } = string.Empty;

        public string? Description { get; set; }
        public bool IsFeatured { get; set; } = false;
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public ICollection<Product> Products { get; set; } = new List<Product>();
    }
}
