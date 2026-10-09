using System.Globalization;
using Algara.Data.Data;
using Algara.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Algara.UnitTests.Models;

public class ProductDataTests
{
    [Theory]
    [InlineData("bg")]
    [InlineData("bg-BG")]
    [InlineData("de")]
    [InlineData("")]
    public void OtherLanguagesUseOriginalProductFields(string language)
    {
        var product = TranslatedProduct();

        Assert.Equal("Диван", product.GetName(language));
        Assert.Equal("Мек диван", product.GetDescription(language));
        Assert.Equal("Дъб", product.GetMaterials(language));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("en-GB")]
    [InlineData("EN-us")]
    public void EnglishUsesAvailableTranslatedFields(string language)
    {
        var product = TranslatedProduct();

        Assert.Equal("Sofa", product.GetName(language));
        Assert.Equal("Soft sofa", product.GetDescription(language));
        Assert.Equal("Oak", product.GetMaterials(language));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\r\n")]
    public void MissingTranslationFallsBackSeparatelyForEachField(string? missing)
    {
        var product = TranslatedProduct();
        var translation = product.Translations.Single();
        translation.Name = missing;
        translation.Materials = missing;

        Assert.Equal("Диван", product.GetName("en"));
        Assert.Equal("Soft sofa", product.GetDescription("en"));
        Assert.Equal("Дъб", product.GetMaterials("en"));
        translation.Description = missing;
        Assert.Equal("Мек диван", product.GetDescription("en"));
    }

    [Fact]
    public void NoTranslationKeepsOriginalAndNullValues()
    {
        var product = new Product { Name = "Диван" };

        Assert.Equal("Диван", product.GetName("en"));
        Assert.Null(product.GetDescription("en"));
        Assert.Null(product.GetMaterials("en"));
    }

    [Fact]
    public void DefaultLanguageUsesUICultureAndNeverChangesStoredFields()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            var product = TranslatedProduct();
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-GB");
            Assert.Equal("Sofa", product.GetName());
            Assert.Equal("Диван", product.Name);
            Assert.Equal("Мек диван", product.Description);
            Assert.Equal("Дъб", product.Materials);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("bg-BG");
            Assert.Equal("Диван", product.GetName());
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public void MainImageUsesOrderThenStableIdWithoutReorderingCollection()
    {
        var product = new Product
        {
            ImageUrl = "/legacy.jpg",
            Images =
            [
                new() { N = 4, DisplayOrder = 10, ImageUrl = "/last.jpg" },
                new() { N = 3, DisplayOrder = 0, ImageUrl = "/second.jpg" },
                new() { N = 2, DisplayOrder = 0, ImageUrl = "/first.jpg" }
            ]
        };

        Assert.Equal("/first.jpg", product.GetMainImageUrl());
        Assert.Equal(new[] { 4, 3, 2 }, product.Images.Select(i => i.N));
        product.Images.Clear();
        Assert.Equal("/legacy.jpg", product.GetMainImageUrl());
        product.ImageUrl = null;
        Assert.Null(product.GetMainImageUrl());
    }

    [Theory]
    [InlineData(" abc-09_x ", "ABC-09_X")]
    [InlineData("000001", "000001")]
    [InlineData(null, "")]
    public void SkuNormalizationTrimsAndUppercases(string? value, string expected)
        => Assert.Equal(expected, ProductSku.Normalize(value));

    [Theory]
    [InlineData("000001", true)]
    [InlineData("SOFA-01_A", true)]
    [InlineData("a", false)]
    [InlineData("А1", false)]
    [InlineData("A B", false)]
    [InlineData(" A", false)]
    [InlineData("A ", false)]
    [InlineData("A/1", false)]
    [InlineData("A\n", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void SkuValidationAllowsOnlyCanonicalAsciiCode(string? value, bool expected)
        => Assert.Equal(expected, ProductSku.IsValid(value));

    [Fact]
    public void SkuMaximumLengthIs64()
    {
        Assert.True(ProductSku.IsValid(new string('A', 64)));
        Assert.False(ProductSku.IsValid(new string('A', 65)));
    }

    [Theory]
    [InlineData(1L, "000001")]
    [InlineData(999999L, "999999")]
    [InlineData(1000000L, "1000000")]
    [InlineData(long.MaxValue, "9223372036854775807")]
    public void AutomaticSkuPreservesLeadingZerosWithoutSixDigitLimit(long number, string expected)
        => Assert.Equal(expected, ProductSku.FormatNumber(number));

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void AutomaticSkuRejectsNonPositiveSequenceValues(long number)
        => Assert.Throws<ArgumentOutOfRangeException>(() => ProductSku.FormatNumber(number));

    [Fact]
    public void DatabaseModelProtectsSkuTranslationsDimensionsAndGallery()
    {
        using var context = new ShopDbContext(new DbContextOptionsBuilder<ShopDbContext>()
            .UseSqlServer("Server=(local);Database=NeverOpenedProductDataUnitTest;Integrated Security=True;TrustServerCertificate=True")
            .Options);
        var model = context.GetService<IDesignTimeModel>().Model;
        var product = model.FindEntityType(typeof(Product))!;
        var sku = product.FindProperty(nameof(Product.Sku))!;
        Assert.False(sku.IsNullable);
        Assert.Equal(64, sku.GetMaxLength());
        Assert.Equal("Latin1_General_100_CI_AS", sku.GetCollation());
        Assert.Contains(product.GetIndexes(), i => i.IsUnique && i.Properties.Single().Name == nameof(Product.Sku));
        Assert.Equal("decimal(10,2)", product.FindProperty(nameof(Product.WidthCm))!.GetColumnType());
        Assert.Equal("decimal(10,2)", product.FindProperty(nameof(Product.HeightCm))!.GetColumnType());
        Assert.Equal("decimal(10,2)", product.FindProperty(nameof(Product.DepthCm))!.GetColumnType());
        Assert.Contains(product.GetCheckConstraints(), c => c.Name == "CK_Products_Sku");
        Assert.Contains(product.GetCheckConstraints(), c => c.Name == "CK_Products_WidthCm");
        Assert.NotNull(model.FindSequence("ProductSkuNumbers"));

        var translation = model.FindEntityType(typeof(ProductTranslation))!;
        Assert.Equal(new[] { "ProductN", "Language" }, translation.FindPrimaryKey()!.Properties.Select(p => p.Name));
        Assert.Equal(300, translation.FindProperty(nameof(ProductTranslation.Name))!.GetMaxLength());
        Assert.Equal(DeleteBehavior.Cascade, translation.GetForeignKeys().Single().DeleteBehavior);
        var image = model.FindEntityType(typeof(ProductImage))!;
        Assert.Equal(2048, image.FindProperty(nameof(ProductImage.ImageUrl))!.GetMaxLength());
        Assert.Equal(DeleteBehavior.Cascade, image.GetForeignKeys().Single().DeleteBehavior);
    }

    private static Product TranslatedProduct() => new()
    {
        Name = "Диван", Description = "Мек диван", Materials = "Дъб",
        Translations = [new() { Language = "en", Name = "Sofa", Description = "Soft sofa", Materials = "Oak" }]
    };
}
