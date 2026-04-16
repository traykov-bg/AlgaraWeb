namespace Algara.Web.ViewModels.Admin
{
    /// <summary>
    /// Един ред в таблицата с продукти за промоция.
    /// При Post съдържанието идва от form array-ите ProductRows[i].*
    /// </summary>
    public class AdminPromotionProductRowViewModel
    {
        public int ProductN { get; set; }

        /// <summary>Маркиран ли е редът за включване в промоцията.</summary>
        public bool Included { get; set; }

        /// <summary>Текуща цена на продукта (Product.Price) — за UI, не се записва.</summary>
        public decimal CurrentPrice { get; set; }

        /// <summary>Снапшот на цената към запис на промоцията (ползва се като „преди отстъпка").</summary>
        public decimal OriginalPrice { get; set; }

        /// <summary>Крайна цена след отстъпка.</summary>
        public decimal PromoPrice { get; set; }

        /// <summary>Процент отстъпка (пази се за точно представяне).</summary>
        public decimal DiscountPercent { get; set; }

        /// <summary>Кратка бележка към реда — напр. „За всеки диван допълнителни 2 табуретки".</summary>
        public string? Note { get; set; }

        // ── Само за рендер на формата, не се пост-ват обратно ──
        public string ProductName { get; set; } = string.Empty;
        public string? ImageUrl { get; set; }

        /// <summary>Име на друга активна промоция, която се припокрива с тази (за warning). null ако няма.</summary>
        public string? OverlappingPromotionName { get; set; }
    }
}
