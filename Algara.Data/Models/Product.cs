namespace Algara.Data.Models
{
    public class Product
    {
        public int N { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public decimal Price { get; set; }
        public string? ImageUrl { get; set; }
        public bool IsCustomizable { get; set; }
        public bool IsFeatured { get; set; } = false;
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public int? CategoryN { get; set; }
        public Category? Category { get; set; }

        public ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();
        public ICollection<ProductSubCategory> ProductSubCategories { get; set; } = new List<ProductSubCategory>();
        public ICollection<ProductPromotion> ProductPromotions { get; set; } = new List<ProductPromotion>();

        /// <summary>
        /// Връща активния ProductPromotion за дадения момент —
        /// този, който дава най-ниска крайна цена (най-голяма отстъпка).
        /// </summary>
        public ProductPromotion? GetActivePromotion(DateTime now)
        {
            return ProductPromotions
                .Where(pp => pp.Promotion != null &&
                             pp.Promotion.IsActive &&
                             pp.Promotion.StartDate <= now &&
                             pp.Promotion.EndDate >= now)
                .OrderBy(pp => pp.PromoPrice)
                .FirstOrDefault();
        }

        /// <summary>Активния процент отстъпка (за показване на баджа). null ако няма активна промоция.</summary>
        public decimal? GetActiveDiscount(DateTime now)
        {
            var pp = GetActivePromotion(now);
            return pp != null ? pp.DiscountPercent : null;
        }

        /// <summary>Крайната цена след отстъпка — чете се директно от ProductPromotion.PromoPrice.</summary>
        public decimal GetDiscountedPrice(DateTime now)
        {
            var pp = GetActivePromotion(now);
            return pp != null ? pp.PromoPrice : Price;
        }

        /// <summary>Етикет "-X%" или "-X €" според типа на активната промоция.</summary>
        public string GetDiscountLabel(DateTime now)
        {
            var pp = GetActivePromotion(now);
            return pp != null ? pp.GetDiscountLabel() : string.Empty;
        }
    }
}
