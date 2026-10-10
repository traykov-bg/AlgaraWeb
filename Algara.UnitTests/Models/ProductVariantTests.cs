using Algara.Data.Data;
using Algara.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Algara.UnitTests.Models;

public class ProductVariantTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 12, 0, 0);

    [Fact]
    public void PromotionDiscountsOnlySavedBaseAndKeepsCurrentFullColorSurcharge()
    {
        var (product, variant) = ColorProduct();
        product.Price = 200m;
        variant.Surcharge = 12.34m;
        product.ProductPromotions.Add(ActivePromotion());
        Assert.Equal(102.35m, product.GetVariantPrice(variant, Now));
        Assert.Equal(112.34m, product.GetVariantOriginalPrice(variant, Now));
        variant.Surcharge = 20m;
        Assert.Equal(110.01m, product.GetVariantPrice(variant, Now));
        Assert.Equal(120m, product.GetVariantOriginalPrice(variant, Now));
        Assert.Equal(90.01m, product.GetDiscountedPrice(Now));
        Assert.Equal(100m, product.ProductPromotions.Single().OriginalPrice);
    }

    [Fact]
    public void WithoutActivePromotionColorUsesCatalogBasePriceAndSurcharge()
    {
        var (product, variant) = ColorProduct();
        product.Price = 105.67m;
        variant.Surcharge = 10.23m;
        var promotion = ActivePromotion();
        promotion.Promotion.EndDate = Now.AddSeconds(-1);
        product.ProductPromotions.Add(promotion);
        Assert.Equal(115.90m, product.GetVariantPrice(variant, Now));
        Assert.Equal(115.90m, product.GetVariantOriginalPrice(variant, Now));
    }

    [Fact]
    public void EveryColorOfFabricUsesSharedCurrentProductFabricSurcharge()
    {
        var (product, first, second) = FabricProduct();
        first.Surcharge = 999m;
        second.Surcharge = 1m;
        product.ProductPromotions.Add(ActivePromotion());
        Assert.Equal(80m, product.GetVariantSurcharge(first));
        Assert.Equal(80m, product.GetVariantSurcharge(second));
        Assert.Equal(170.01m, product.GetVariantPrice(first, Now));
        product.FabricSurcharges.Single().Surcharge = 100m;
        Assert.Equal(190.01m, product.GetVariantPrice(first, Now));
        Assert.Equal(190.01m, product.GetVariantPrice(second, Now));
    }

    [Fact]
    public void FabricSurchargeIsSpecificToProduct()
    {
        var (first, firstVariant, _) = FabricProduct();
        var (second, secondVariant, _) = FabricProduct();
        second.N = 2;
        secondVariant.ProductN = 2;
        second.FabricSurcharges.Single().ProductN = 2;
        second.FabricSurcharges.Single().Surcharge = 25m;
        Assert.Equal(80m, first.GetVariantSurcharge(firstVariant));
        Assert.Equal(25m, second.GetVariantSurcharge(secondVariant));
        Assert.False(first.IsSelectableVariant(secondVariant));
        Assert.Throws<ArgumentException>(() => first.GetVariantPrice(secondVariant, Now));
    }

    [Fact]
    public void MissingFabricPriceCannotSilentlyProduceFreeUpgrade()
    {
        var (product, variant, _) = FabricProduct();
        product.FabricSurcharges.Clear();
        Assert.False(product.IsSelectableVariant(variant));
        Assert.Empty(product.GetSelectableVariants());
        Assert.Throws<ArgumentException>(() => product.GetVariantPrice(variant, Now));
    }

    [Theory]
    [InlineData("variant")]
    [InlineData("color")]
    [InlineData("fabric")]
    public void InactiveSelectionCannotBePricedOrSelected(string inactivePart)
    {
        var (product, variant, _) = FabricProduct();
        if (inactivePart == "variant") variant.IsActive = false;
        if (inactivePart == "color") variant.Color.IsActive = false;
        if (inactivePart == "fabric") variant.Color.Fabric!.IsActive = false;
        Assert.False(product.IsSelectableVariant(variant));
        Assert.Throws<ArgumentException>(() => product.GetVariantSurcharge(variant));
        Assert.Equal(ProductAvailability.Unknown, product.GetAvailability(variant));
    }

    [Fact]
    public void ProductOptionKindControlsSelectionWithoutCategoryNameHeuristics()
    {
        var (product, variant) = ColorProduct();
        product.Category = new Category { Name = "Sofas" };
        product.IsCustomizable = true;
        Assert.True(product.IsSelectableVariant(variant));
        product.OptionKind = ProductOptionKind.Fabric;
        Assert.False(product.IsSelectableVariant(variant));
        product.OptionKind = ProductOptionKind.None;
        Assert.False(product.IsSelectableVariant(variant));
        var (fabricProduct, fabricVariant, _) = FabricProduct();
        fabricProduct.OptionKind = ProductOptionKind.Color;
        Assert.False(fabricProduct.IsSelectableVariant(fabricVariant));
    }

    [Fact]
    public void DefaultSelectionFiltersInactiveVariantsAndUsesStableFallback()
    {
        var (product, first, second) = FabricProduct();
        second.IsDefault = true;
        Assert.Same(second, product.GetDefaultVariant());
        second.IsActive = false;
        Assert.Same(first, product.GetDefaultVariant());
        first.IsActive = false;
        Assert.Null(product.GetDefaultVariant());
    }

    [Fact]
    public void ExactVariantPhotoFallsBackToBaseWhenNextSelectionHasNoPhoto()
    {
        var (product, first, second) = FabricProduct();
        product.Images.Add(new ProductImage { N = 1, ProductN = product.N, ImageUrl = "/base.jpg", DisplayOrder = 0 });
        product.Images.Add(new ProductImage { N = 2, ProductN = product.N, ImageUrl = "/exact.jpg", DisplayOrder = 1 });
        first.ProductImageN = 2;
        Assert.Equal("/exact.jpg", product.GetVariantImageUrl(first));
        Assert.Equal("/base.jpg", product.GetVariantImageUrl(second));
        Assert.Equal("/base.jpg", product.GetVariantImageUrl(null));
        Assert.Equal("/exact.jpg", product.GetVariantImageUrl(first));
        Assert.Equal("/base.jpg", product.GetMainImageUrl());
    }

    [Fact]
    public void VariantPhotoCannotUseAnotherProductsGallery()
    {
        var (product, variant) = ColorProduct();
        product.ImageUrl = "/legacy.jpg";
        variant.ProductImageN = 9;
        variant.Image = new ProductImage { N = 9, ProductN = 999, ImageUrl = "/foreign.jpg" };
        Assert.Equal("/legacy.jpg", product.GetVariantImageUrl(variant));
        variant.Image.ProductN = product.N;
        Assert.Equal("/foreign.jpg", product.GetVariantImageUrl(variant));
        variant.IsActive = false;
        Assert.Equal("/legacy.jpg", product.GetVariantImageUrl(variant));
    }

    [Theory]
    [InlineData(null, false, ProductAvailability.Unknown)]
    [InlineData(null, true, ProductAvailability.Unknown)]
    [InlineData(1, false, ProductAvailability.InStock)]
    [InlineData(2, true, ProductAvailability.InStock)]
    [InlineData(0, false, ProductAvailability.Inquiry)]
    [InlineData(0, true, ProductAvailability.MadeToOrder)]
    public void AvailabilityUsesStockAndExplicitFurnitureType(int? stock, bool upholstered, ProductAvailability expected)
    {
        var product = new Product { StockQuantity = stock, IsUpholstered = upholstered, IsCustomizable = !upholstered };
        Assert.Equal(expected, product.GetAvailability());
        var (variantProduct, variant) = ColorProduct();
        variantProduct.IsUpholstered = upholstered;
        variant.StockQuantity = stock;
        Assert.Equal(expected, variantProduct.GetAvailability(variant));
        Assert.Equal(ProductAvailability.Unknown, variantProduct.GetAvailability());
    }

    [Fact]
    public void MissingStockRemainsUnknownAndSharedTermsSeparateManufacturingFromDelivery()
    {
        Assert.Null(new Product().StockQuantity);
        Assert.Null(new ProductVariant().StockQuantity);
        Assert.Equal(ProductAvailability.Unknown, new Product { IsUpholstered = true }.GetAvailability());
        Assert.Equal(5, DeliveryPolicy.DeliveryWorkingDays);
        Assert.Equal(30, DeliveryPolicy.ManufacturingWorkingDays);
        Assert.Throws<ArgumentOutOfRangeException>(() => DeliveryPolicy.GetAvailability(-1, true));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("EN-gb")]
    public void CatalogEnglishFallbackIsPerField(string language)
    {
        var fabric = new Fabric { Name = "Дамаска", EnglishName = "Fabric", Description = "Описание", EnglishDescription = " " };
        var color = new CatalogColor { Name = "Синьо", EnglishName = "Blue" };
        Assert.Equal("Fabric", fabric.GetName(language));
        Assert.Equal("Описание", fabric.GetDescription(language));
        Assert.Equal("Blue", color.GetName(language));
        color.EnglishName = null;
        fabric.EnglishName = "\t";
        Assert.Equal("Синьо", color.GetName(language));
        Assert.Equal("Дамаска", fabric.GetName(language));
        Assert.Equal("Дамаска", fabric.GetName("bg"));
        Assert.Equal("Синьо", color.GetName("de"));
    }

    [Fact]
    public void DatabaseModelConstrainsOwnershipStockPricesCodesAndConcurrency()
    {
        using var context = new ShopDbContext(new DbContextOptionsBuilder<ShopDbContext>()
            .UseSqlServer("Server=(local);Database=NeverOpenedVariantModelTest;Integrated Security=True;TrustServerCertificate=True")
            .Options);
        var model = context.GetService<IDesignTimeModel>().Model;
        var product = model.FindEntityType(typeof(Product))!;
        var rowVersion = product.FindProperty(nameof(Product.RowVersion))!;
        Assert.True(rowVersion.IsConcurrencyToken);
        Assert.Equal(ValueGenerated.OnAddOrUpdate, rowVersion.ValueGenerated);
        Assert.True(product.FindProperty(nameof(Product.StockQuantity))!.IsNullable);
        Assert.Contains(product.GetCheckConstraints(), c => c.Name == "CK_Products_VariantStock");
        var variant = model.FindEntityType(typeof(ProductVariant))!;
        Assert.Contains(variant.GetIndexes(), i => i.IsUnique && i.Properties.Select(p => p.Name).SequenceEqual(["ProductN", "CatalogColorN"]));
        Assert.Contains(variant.GetIndexes(), i => i.IsUnique && i.Properties.Select(p => p.Name).SequenceEqual(["Sku"]));
        Assert.Equal("decimal(18,2)", variant.FindProperty(nameof(ProductVariant.Surcharge))!.GetColumnType());
        Assert.Contains(variant.GetCheckConstraints(), c => c.Name == "CK_ProductVariants_StockQuantity");
        var imageKey = variant.GetForeignKeys().Single(k => k.PrincipalEntityType.ClrType == typeof(ProductImage));
        Assert.Equal(new[] { "ProductN", "ProductImageN" }, imageKey.Properties.Select(p => p.Name));
        Assert.Equal(new[] { "ProductN", "N" }, imageKey.PrincipalKey.Properties.Select(p => p.Name));
        Assert.False(imageKey.IsRequired);
        Assert.Equal(DeleteBehavior.Restrict, imageKey.DeleteBehavior);
        Assert.Single(variant.GetForeignKeys(), k => k.DeleteBehavior == DeleteBehavior.Cascade);
        var color = model.FindEntityType(typeof(CatalogColor))!;
        var codeIndex = color.GetIndexes().Single(i => i.IsUnique);
        Assert.Equal(new[] { "FabricN", "Code" }, codeIndex.Properties.Select(p => p.Name));
        Assert.Null(codeIndex.GetFilter());
        Assert.Equal(DeleteBehavior.Restrict, color.GetForeignKeys().Single().DeleteBehavior);
        var surcharge = model.FindEntityType(typeof(ProductFabricSurcharge))!;
        Assert.Equal(new[] { "ProductN", "FabricN" }, surcharge.FindPrimaryKey()!.Properties.Select(p => p.Name));
        Assert.Equal("decimal(18,2)", surcharge.FindProperty(nameof(ProductFabricSurcharge.Surcharge))!.GetColumnType());
        Assert.NotNull(model.FindSequence("ProductVariantSkuNumbers"));
    }

    [Fact]
    public void CatalogPriceUsesCheapestSelectableVariantAfterBaseChoiceIsDisabled()
    {
        var (product, first, second) = FabricProduct();
        first.Color.FabricN = null;
        first.Color.Fabric = null;
        second.Color.FabricN = null;
        second.Color.Fabric = null;
        product.OptionKind = ProductOptionKind.Color;
        first.Surcharge = 0m;
        first.IsDefault = true;
        second.Surcharge = 25m;
        Assert.Equal(100m, product.GetCatalogPrice(Now));
        first.Color.IsActive = false;
        Assert.Equal(125m, product.GetCatalogPrice(Now));
        Assert.Equal(125m, product.GetCatalogOriginalPrice(Now));
        second.IsActive = false;
        Assert.Equal(100m, product.GetCatalogPrice(Now));
    }

    [Fact]
    public void CatalogPromoUsesSavedBasePlusCheapestCurrentSelectableSurcharge()
    {
        var (product, first, second) = FabricProduct();
        product.Price = 999m;
        first.IsDefault = true;
        first.IsActive = false;
        product.ProductPromotions.Add(ActivePromotion());
        Assert.Equal(170.01m, product.GetCatalogPrice(Now));
        Assert.Equal(180m, product.GetCatalogOriginalPrice(Now));
        product.FabricSurcharges.Single().Surcharge = 95m;
        Assert.Equal(185.01m, product.GetCatalogPrice(Now));
        Assert.Equal(195m, product.GetCatalogOriginalPrice(Now));
        second.IsActive = false;
        Assert.Equal(90.01m, product.GetCatalogPrice(Now));
        Assert.Equal(100m, product.GetCatalogOriginalPrice(Now));
    }

    private static (Product Product, ProductVariant Variant) ColorProduct()
    {
        var product = new Product { N = 1, Price = 100m, OptionKind = ProductOptionKind.Color };
        var variant = new ProductVariant { N = 10, ProductN = 1, Product = product, CatalogColorN = 20, Color = new CatalogColor { N = 20, Code = "BLUE", Name = "Blue" } };
        product.Variants.Add(variant);
        return (product, variant);
    }

    private static (Product Product, ProductVariant First, ProductVariant Second) FabricProduct()
    {
        var product = new Product { N = 1, Price = 100m, OptionKind = ProductOptionKind.Fabric, IsUpholstered = true };
        var fabric = new Fabric { N = 5, Code = "VELVET", Name = "Velvet" };
        product.FabricSurcharges.Add(new ProductFabricSurcharge { ProductN = 1, Product = product, FabricN = 5, Fabric = fabric, Surcharge = 80m });
        var first = new ProductVariant { N = 10, ProductN = 1, Product = product, CatalogColorN = 20, Color = new CatalogColor { N = 20, FabricN = 5, Fabric = fabric, Code = "BLUE", Name = "Blue" } };
        var second = new ProductVariant { N = 11, ProductN = 1, Product = product, CatalogColorN = 21, Color = new CatalogColor { N = 21, FabricN = 5, Fabric = fabric, Code = "RED", Name = "Red" } };
        product.Variants.Add(first);
        product.Variants.Add(second);
        return (product, first, second);
    }

    private static ProductPromotion ActivePromotion() => new()
    {
        OriginalPrice = 100m, PromoPrice = 90.01m, DiscountPercent = 9.99m,
        Promotion = new Promotion { IsActive = true, StartDate = Now.AddDays(-1), EndDate = Now.AddDays(1) }
    };
}
