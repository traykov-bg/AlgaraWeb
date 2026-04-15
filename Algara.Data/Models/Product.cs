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

        /// <summary>Връща активния процент отстъпка към дадения момент (най-висок, ако има повече от една промоция).</summary>
        public decimal? GetActiveDiscount(DateTime now)
        {
            var best = ProductPromotions
                .Where(pp => pp.Promotion != null &&
                             pp.Promotion.IsActive &&
                             pp.Promotion.StartDate <= now &&
                             pp.Promotion.EndDate >= now)
                .Select(pp => pp.Promotion.DiscountPercent)
                .DefaultIfEmpty(0)
                .Max();
            return best > 0 ? best : null;
        }

        /// <summary>Изчислена цена след отстъпка. Ако няма активна промоция — стандартната цена.</summary>
        public decimal GetDiscountedPrice(DateTime now)
        {
            var pct = GetActiveDiscount(now);
            return pct.HasValue ? Math.Round(Price * (1 - pct.Value / 100m), 2) : Price;
        }

        public string GetDiscountLabel(DateTime now)
        {
            var pct = GetActiveDiscount(now);
            if (!pct.HasValue) return string.Empty;
            // показва цяло число ако няма дробна част, иначе до 3 знака без trailing zeros
            return "-" + (pct.Value == Math.Floor(pct.Value)
                ? ((int)pct.Value).ToString()
                : pct.Value.ToString("G29").TrimEnd('0').TrimEnd('.')) + "%";
        }
    }
}
