namespace Algara.Data.Models
{
    public class ProductSubCategory
    {
        public int ProductN { get; set; }
        public int SubCategoryN { get; set; }

        public Product Product { get; set; } = null!;
        public SubCategory SubCategory { get; set; } = null!;
    }
}
