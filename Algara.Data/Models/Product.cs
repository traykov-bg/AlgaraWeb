namespace Algara.Data.Models
{
    public class Product
    {
        public int N { get; set; }
        public string Sku { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? Materials { get; set; }
        public decimal? WidthCm { get; set; }
        public decimal? HeightCm { get; set; }
        public decimal? DepthCm { get; set; }
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
        public ICollection<ProductTranslation> Translations { get; set; } = new List<ProductTranslation>();
        public ICollection<ProductImage> Images { get; set; } = new List<ProductImage>();

        public string GetName(string? language = null)
            => TranslatedValue(GetTranslation(language)?.Name, Name)!;

        public string? GetDescription(string? language = null)
            => TranslatedValue(GetTranslation(language)?.Description, Description);

        public string? GetMaterials(string? language = null)
            => TranslatedValue(GetTranslation(language)?.Materials, Materials);

        public string? GetMainImageUrl()
            => Images.OrderBy(i => i.DisplayOrder).ThenBy(i => i.N)
                .Select(i => i.ImageUrl).FirstOrDefault() ?? ImageUrl;

        private ProductTranslation? GetTranslation(string? language)
        {
            language ??= System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            return (language.Equals("en", StringComparison.OrdinalIgnoreCase) ||
                    language.StartsWith("en-", StringComparison.OrdinalIgnoreCase))
                ? Translations.FirstOrDefault(t => t.Language.Equals("en", StringComparison.OrdinalIgnoreCase))
                : null;
        }

        private static string? TranslatedValue(string? translation, string? original)
            => string.IsNullOrWhiteSpace(translation) ? original : translation;

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
