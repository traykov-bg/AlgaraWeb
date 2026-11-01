namespace Algara.Data.Models;

public sealed record PackedParcel(decimal WeightKg, decimal WidthCm, decimal HeightCm, decimal DepthCm);
public sealed record ProductShippingError(string Field, string Message);

public static class ProductShippingRules
{
    public const int MaxParcels = 50;
    public const decimal MaxWeightKg = 100000m;
    public const decimal MaxDimensionCm = 100000m;

    public static IReadOnlyList<ProductShippingError> Validate(Product product)
    {
        var errors = new List<ProductShippingError>();
        var rows = product.ShippingParcels.OrderBy(p => p.Position).ToList();
        if (rows.Count > MaxParcels)
            errors.Add(new("ShippingParcels", $"Един продукт може да има до {MaxParcels} колета."));
        if (rows.Count == 0 && (product.AllowsCourier || product.UseCatalogDimensionsForShipping))
            errors.Add(new("ShippingParcels", "Добавете поне един колет с тегло с опаковката и транспортни размери."));
        if (rows.Select(p => p.Position).Where((position, index) => position != index + 1).Any())
            errors.Add(new("ShippingParcels", "Номерата на колетите трябва да са последователни, започвайки от 1."));

        if (product.UseCatalogDimensionsForShipping)
        {
            if (rows.Count != 1)
                errors.Add(new("UseCatalogDimensionsForShipping", "Размерите от каталога могат да се използват само за един колет."));
            if (!ValidDimension(product.WidthCm) || !ValidDimension(product.HeightCm) || !ValidDimension(product.DepthCm))
                errors.Add(new("UseCatalogDimensionsForShipping", "Попълнете трите каталогови размера с точност до 0,01 см, преди да потвърдите съвпадението с опаковката."));
        }

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var prefix = $"ShippingParcels[{i}]";
            if (row.WeightKg < 0.001m || row.WeightKg > MaxWeightKg || decimal.Round(row.WeightKg, 3) != row.WeightKg)
                errors.Add(new(prefix + ".WeightKg", "Въведете тегло с опаковката от 0,001 до 100 000 кг с най-много три знака след десетичната запетая."));
            if (product.UseCatalogDimensionsForShipping)
            {
                if (row.WidthCm.HasValue || row.HeightCm.HasValue || row.DepthCm.HasValue)
                    errors.Add(new(prefix, "При потвърдено използване на каталоговите размери отделните транспортни размери трябва да са празни."));
            }
            else
            {
                if (!ValidDimension(row.WidthCm)) errors.Add(new(prefix + ".WidthCm", DimensionMessage));
                if (!ValidDimension(row.HeightCm)) errors.Add(new(prefix + ".HeightCm", DimensionMessage));
                if (!ValidDimension(row.DepthCm)) errors.Add(new(prefix + ".DepthCm", DimensionMessage));
            }
        }
        return errors;
    }

    // No inference from furniture dimensions or IsUpholstered. The caller separately
    // checks AllowsCourier; valid parcels may also describe own-transport products.
    public static bool TryGetParcels(Product product, out IReadOnlyList<PackedParcel> parcels)
    {
        parcels = [];
        if (product.ShippingParcels.Count == 0 || Validate(product).Count > 0) return false;
        parcels = product.ShippingParcels.OrderBy(p => p.Position).Select(p => new PackedParcel(
            p.WeightKg,
            (product.UseCatalogDimensionsForShipping ? product.WidthCm : p.WidthCm)!.Value,
            (product.UseCatalogDimensionsForShipping ? product.HeightCm : p.HeightCm)!.Value,
            (product.UseCatalogDimensionsForShipping ? product.DepthCm : p.DepthCm)!.Value)).ToList();
        return true;
    }

    private const string DimensionMessage = "Въведете транспортен размер от 0,01 до 100 000 см с най-много два знака след десетичната запетая.";
    private static bool ValidDimension(decimal? value)
        => value is >= 0.01m and <= MaxDimensionCm && decimal.Round(value.Value, 2) == value.Value;
}
