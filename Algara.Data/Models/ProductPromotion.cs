namespace Algara.Data.Models
{
    public class ProductPromotion
    {
        public int ProductN { get; set; }
        public Product Product { get; set; } = null!;

        public int PromotionN { get; set; }
        public Promotion Promotion { get; set; } = null!;

        /// <summary>
        /// Snapshot на цената на продукта към момента на запис на промоцията.
        /// Ако продуктът промени цената си по-късно — промоцията продължава
        /// да ползва тази стойност (брошурен режим).
        /// </summary>
        public decimal OriginalPrice { get; set; }

        /// <summary>Крайна цена след отстъпка. Главната колона — четена в публичните view-ове.</summary>
        public decimal PromoPrice { get; set; }

        /// <summary>Процент отстъпка — пази се за да се избегне rounding-дрифт при показване.</summary>
        public decimal DiscountPercent { get; set; }

        /// <summary>
        /// Кратка бележка към реда (напр. „За всеки диван допълнителни 2 табуретки").
        /// Показва се в каталога и в детайла на продукта. Опционална.
        /// </summary>
        public string? Note { get; set; }

        /// <summary>Сумата отстъпка в €: OriginalPrice − PromoPrice.</summary>
        public decimal DiscountAmount => OriginalPrice - PromoPrice;

        /// <summary>Етикет според типа на промоцията: "-15%" или "-20 €".</summary>
        public string GetDiscountLabel()
        {
            if (Promotion != null && Promotion.Type == PromotionType.Amount)
            {
                var amt = DiscountAmount;
                var s = amt == Math.Floor(amt) ? ((int)amt).ToString() : amt.ToString("0.00");
                return "-" + s + " €";
            }
            var pct = DiscountPercent;
            var ps = pct == Math.Floor(pct)
                ? ((int)pct).ToString()
                : pct.ToString("G29").TrimEnd('0').TrimEnd('.');
            return "-" + ps + "%";
        }
    }
}
