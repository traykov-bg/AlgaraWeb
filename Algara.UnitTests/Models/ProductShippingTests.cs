using Algara.Data.Data;
using Algara.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Algara.UnitTests.Models;

public sealed class ProductShippingTests
{
    [Fact]
    public void LegacyProductDoesNotAcquireCourierEligibilityOrInventParcels()
    {
        var product = new Product { WidthCm = 10, HeightCm = 20, DepthCm = 30, IsUpholstered = true };
        Assert.False(product.AllowsCourier);
        Assert.False(product.UseCatalogDimensionsForShipping);
        Assert.Empty(ProductShippingRules.Validate(product));
        Assert.False(ProductShippingRules.TryGetParcels(product, out var parcels));
        Assert.Empty(parcels);
    }

    [Fact]
    public void CourierEligibilityRequiresCompleteShippingData()
    {
        var product = new Product { AllowsCourier = true };
        Assert.Contains(ProductShippingRules.Validate(product), e => e.Field == "ShippingParcels");
        Assert.False(ProductShippingRules.TryGetParcels(product, out _));
    }

    [Fact]
    public void CatalogDimensionsAreNeverUsedWithoutExplicitConfirmation()
    {
        var product = CatalogProduct();
        Assert.False(ProductShippingRules.TryGetParcels(product, out _));
        Assert.Contains(ProductShippingRules.Validate(product), e => e.Field == "ShippingParcels[0].WidthCm");
        product.UseCatalogDimensionsForShipping = true;
        Assert.True(ProductShippingRules.TryGetParcels(product, out var parcels));
        Assert.Equal(new PackedParcel(0.125m, 10.25m, 20.5m, 30.75m), Assert.Single(parcels));
        Assert.Null(product.ShippingParcels.Single().WidthCm);
    }

    [Fact]
    public void CatalogReuseRejectsMultipleParcelsOrMissingDimensions()
    {
        var product = CatalogProduct();
        product.UseCatalogDimensionsForShipping = true;
        product.HeightCm = null;
        Assert.False(ProductShippingRules.TryGetParcels(product, out _));
        product.HeightCm = 20;
        product.ShippingParcels.Add(new() { Position = 2, WeightKg = 1 });
        Assert.Contains(ProductShippingRules.Validate(product), e => e.Field == "UseCatalogDimensionsForShipping");
    }

    [Fact]
    public void CatalogReuseRejectsConflictingExplicitDimensions()
    {
        var product = CatalogProduct();
        product.UseCatalogDimensionsForShipping = true;
        product.ShippingParcels.Single().WidthCm = 999m;
        Assert.False(ProductShippingRules.TryGetParcels(product, out _));
        Assert.Contains(ProductShippingRules.Validate(product), e => e.Field == "ShippingParcels[0]");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("0.0001")]
    [InlineData("1.0001")]
    [InlineData("100000.001")]
    public void InvalidOrOverpreciseWeightIsRejected(string input)
    {
        var product = ExplicitProduct();
        product.ShippingParcels.Single().WeightKg = decimal.Parse(input, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Contains(ProductShippingRules.Validate(product), e => e.Field == "ShippingParcels[0].WeightKg");
        Assert.False(ProductShippingRules.TryGetParcels(product, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("0.001")]
    [InlineData("1.001")]
    [InlineData("100000.01")]
    public void InvalidOrOverpreciseDimensionIsRejected(string? input)
    {
        var product = ExplicitProduct();
        product.ShippingParcels.Single().WidthCm = input == null ? null : decimal.Parse(input, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Contains(ProductShippingRules.Validate(product), e => e.Field == "ShippingParcels[0].WidthCm");
        Assert.False(ProductShippingRules.TryGetParcels(product, out _));
    }

    [Fact]
    public void EveryParcelKeepsItsOwnWeightAndDimensionsInPositionOrder()
    {
        var product = ExplicitProduct();
        product.ShippingParcels =
        [
            new() { Position = 2, WeightKg = 1.125m, WidthCm = 5.5m, HeightCm = 6.25m, DepthCm = 7.75m },
            product.ShippingParcels.Single()
        ];
        Assert.True(ProductShippingRules.TryGetParcels(product, out var parcels));
        Assert.Equal(new PackedParcel(2.555m, 11, 22, 33), parcels[0]);
        Assert.Equal(new PackedParcel(1.125m, 5.5m, 6.25m, 7.75m), parcels[1]);
        Assert.Equal([2, 1], product.ShippingParcels.Select(p => p.Position));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NonSequentialPositionIsRejected(int position)
    {
        var product = ExplicitProduct();
        product.ShippingParcels.Single().Position = position;
        Assert.False(ProductShippingRules.TryGetParcels(product, out _));
    }

    [Fact]
    public void DuplicatePositionsAndMoreThanFiftyParcelsAreRejected()
    {
        var product = ExplicitProduct();
        product.ShippingParcels.Add(new() { Position = 1, WeightKg = 1, WidthCm = 1, HeightCm = 1, DepthCm = 1 });
        Assert.False(ProductShippingRules.TryGetParcels(product, out _));
        product.ShippingParcels = Enumerable.Range(1, ProductShippingRules.MaxParcels + 1)
            .Select(i => new ProductShippingParcel { Position = i, WeightKg = 1, WidthCm = 1, HeightCm = 1, DepthCm = 1 }).ToList();
        Assert.False(ProductShippingRules.TryGetParcels(product, out _));
    }

    [Fact]
    public void NonCourierMayHaveNoDataButSuppliedDataMustStillBeValid()
    {
        var product = ExplicitProduct();
        product.AllowsCourier = false;
        Assert.True(ProductShippingRules.TryGetParcels(product, out _));
        product.ShippingParcels.Single().HeightCm = null;
        Assert.NotEmpty(ProductShippingRules.Validate(product));
    }

    [Fact]
    public void TechnicalBoundsAreInclusive()
    {
        var product = ExplicitProduct();
        var parcel = product.ShippingParcels.Single();
        parcel.WeightKg = 0.001m;
        parcel.WidthCm = 0.01m;
        parcel.HeightCm = 100000m;
        Assert.True(ProductShippingRules.TryGetParcels(product, out _));
        parcel.WeightKg = 100000m;
        Assert.True(ProductShippingRules.TryGetParcels(product, out _));
    }

    [Fact]
    public void SqlModelPreservesFractionalPrecisionAndUniqueParcelPositions()
    {
        using var context = new ShopDbContext(new DbContextOptionsBuilder<ShopDbContext>()
            .UseSqlServer("Server=(local);Database=NeverOpenedShippingUnitTest;Integrated Security=True;TrustServerCertificate=True").Options);
        var model = context.GetService<IDesignTimeModel>().Model;
        var entity = model.FindEntityType(typeof(ProductShippingParcel))!;
        Assert.Equal(9, entity.FindProperty(nameof(ProductShippingParcel.WeightKg))!.GetPrecision());
        Assert.Equal(3, entity.FindProperty(nameof(ProductShippingParcel.WeightKg))!.GetScale());
        Assert.Equal(2, entity.FindProperty(nameof(ProductShippingParcel.WidthCm))!.GetScale());
        Assert.Contains(entity.GetIndexes(), index => index.IsUnique && index.Properties.Select(p => p.Name).SequenceEqual(["ProductN", "Position"]));
        Assert.Equal(3, entity.GetCheckConstraints().Count());
        Assert.Equal(false, model.FindEntityType(typeof(Product))!.FindProperty(nameof(Product.AllowsCourier))!.GetDefaultValue());
    }

    private static Product CatalogProduct() => new()
    {
        AllowsCourier = true, WidthCm = 10.25m, HeightCm = 20.5m, DepthCm = 30.75m,
        ShippingParcels = [new() { Position = 1, WeightKg = 0.125m }]
    };

    private static Product ExplicitProduct() => new()
    {
        AllowsCourier = true, WidthCm = 999, HeightCm = 999, DepthCm = 999,
        ShippingParcels = [new() { Position = 1, WeightKg = 2.555m, WidthCm = 11, HeightCm = 22, DepthCm = 33 }]
    };
}
