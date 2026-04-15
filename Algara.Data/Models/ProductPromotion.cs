namespace Algara.Data.Models
{
    public class ProductPromotion
    {
        public int ProductN { get; set; }
        public Product Product { get; set; } = null!;

        public int PromotionN { get; set; }
        public Promotion Promotion { get; set; } = null!;
    }
}
