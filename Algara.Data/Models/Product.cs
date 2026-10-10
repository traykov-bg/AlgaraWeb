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
        public ProductOptionKind OptionKind { get; set; }
        public bool IsUpholstered { get; set; }
        public int? StockQuantity { get; set; }
        public byte[] RowVersion { get; set; } = [];
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
        public ICollection<ProductVariant> Variants { get; set; } = new List<ProductVariant>();
        public ICollection<ProductFabricSurcharge> FabricSurcharges { get; set; } = new List<ProductFabricSurcharge>();

        public string GetName(string? language = null)
            => TranslatedValue(GetTranslation(language)?.Name, Name)!;

        public string? GetDescription(string? language = null)
            => TranslatedValue(GetTranslation(language)?.Description, Description);

        public string? GetMaterials(string? language = null)
            => TranslatedValue(GetTranslation(language)?.Materials, Materials);

        public string? GetMainImageUrl()
            => Images.OrderBy(i => i.DisplayOrder).ThenBy(i => i.N)
                .Select(i => i.ImageUrl).FirstOrDefault() ?? ImageUrl;

        public bool IsSelectableVariant(ProductVariant? variant)
        {
            if (variant == null || !variant.IsActive || variant.Color == null || !variant.Color.IsActive ||
                (N > 0 ? variant.ProductN != N : !ReferenceEquals(variant.Product, this) && !Variants.Contains(variant)))
                return false;

            return OptionKind switch
            {
                ProductOptionKind.Color => variant.Color.FabricN == null,
                ProductOptionKind.Fabric => variant.Color.FabricN.HasValue &&
                    variant.Color.Fabric is { IsActive: true } &&
                    FabricSurcharges.Any(s => s.FabricN == variant.Color.FabricN),
                _ => false
            };
        }

        public IEnumerable<ProductVariant> GetSelectableVariants()
            => Variants.Where(IsSelectableVariant).OrderByDescending(v => v.IsDefault).ThenBy(v => v.N);

        public ProductVariant? GetDefaultVariant() => GetSelectableVariants().FirstOrDefault();

        public decimal GetVariantSurcharge(ProductVariant variant)
        {
            if (!IsSelectableVariant(variant))
                throw new ArgumentException("The variant is not selectable for this product.", nameof(variant));

            return OptionKind == ProductOptionKind.Fabric
                ? FabricSurcharges.Single(s => s.FabricN == variant.Color.FabricN).Surcharge
                : variant.Surcharge;
        }

        public decimal GetVariantPrice(ProductVariant variant, DateTime now)
            => GetDiscountedPrice(now) + GetVariantSurcharge(variant);

        public decimal GetVariantOriginalPrice(ProductVariant variant, DateTime now)
            => (GetActivePromotion(now)?.OriginalPrice ?? Price) + GetVariantSurcharge(variant);

        public decimal GetCatalogPrice(DateTime now)
            => GetDiscountedPrice(now) + GetSelectableVariants().Select(GetVariantSurcharge).DefaultIfEmpty(0m).Min();

        public decimal GetCatalogOriginalPrice(DateTime now)
            => (GetActivePromotion(now)?.OriginalPrice ?? Price) + GetSelectableVariants().Select(GetVariantSurcharge).DefaultIfEmpty(0m).Min();

        public string? GetVariantImageUrl(ProductVariant? variant)
        {
            if (!IsSelectableVariant(variant) || !variant!.ProductImageN.HasValue)
                return GetMainImageUrl();

            var exactImage = Images.FirstOrDefault(i => i.N == variant.ProductImageN && i.ProductN == N) ??
                (variant.Image is { } image && image.N == variant.ProductImageN && image.ProductN == N ? image : null);
            return !string.IsNullOrWhiteSpace(exactImage?.ImageUrl) ? exactImage.ImageUrl : GetMainImageUrl();
        }

        public ProductAvailability GetAvailability(ProductVariant? variant = null)
        {
            if (OptionKind == ProductOptionKind.None)
                return variant == null ? DeliveryPolicy.GetAvailability(StockQuantity, IsUpholstered) : ProductAvailability.Unknown;

            return IsSelectableVariant(variant)
                ? DeliveryPolicy.GetAvailability(variant!.StockQuantity, IsUpholstered)
                : ProductAvailability.Unknown;
        }

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
