namespace Algara.Data.Models
{
    public class Promotion
    {
        public int N { get; set; }
        public string Name { get; set; } = string.Empty;
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        /// <summary>Режим: процент или сума (крайна цена).</summary>
        public PromotionType Type { get; set; } = PromotionType.Percent;

        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>ID на потребителя, създал промоцията.</summary>
        public int UserCreated { get; set; }

        public ICollection<ProductPromotion> ProductPromotions { get; set; } = new List<ProductPromotion>();

        /// <summary>
        /// Връща етикет за отстъпката в списъка с промоции:
        /// - еднаква отстъпка на всички редове → една стойност ("-15%", "-20 €")
        /// - различни → диапазон ("-15% – -40%", "-20 € – -100 €")
        /// - без редове → празен стринг
        /// </summary>
        public string GetDiscountRangeLabel()
        {
            if (ProductPromotions == null || ProductPromotions.Count == 0)
                return string.Empty;

            if (Type == PromotionType.Percent)
            {
                var min = ProductPromotions.Min(pp => pp.DiscountPercent);
                var max = ProductPromotions.Max(pp => pp.DiscountPercent);
                return min == max
                    ? "-" + FormatPercent(min) + "%"
                    : "-" + FormatPercent(min) + "% – -" + FormatPercent(max) + "%";
            }
            else
            {
                var min = ProductPromotions.Min(pp => pp.DiscountAmount);
                var max = ProductPromotions.Max(pp => pp.DiscountAmount);
                return min == max
                    ? "-" + FormatMoney(min) + " €"
                    : "-" + FormatMoney(min) + " € – -" + FormatMoney(max) + " €";
            }
        }

        private static string FormatPercent(decimal v)
            => v == Math.Floor(v)
                ? ((int)v).ToString()
                : v.ToString("G29").TrimEnd('0').TrimEnd('.');

        private static string FormatMoney(decimal v)
            => v == Math.Floor(v) ? ((int)v).ToString() : v.ToString("0.00");
    }
}
