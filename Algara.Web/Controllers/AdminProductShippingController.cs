using Algara.Data.Models;
using Algara.Web.ViewModels.Admin;

namespace Algara.Web.Controllers;

public partial class AdminController
{
    private static void ReadShippingForm(AdminProductFormViewModel vm, Product product)
    {
        vm.AllowsCourier = product.AllowsCourier;
        vm.UseCatalogDimensionsForShipping = product.UseCatalogDimensionsForShipping;
        vm.ShippingParcels = product.ShippingParcels.OrderBy(p => p.Position).Select(p => new AdminProductShippingParcelViewModel
        {
            WeightKg = p.WeightKg, WidthCm = p.WidthCm, HeightCm = p.HeightCm, DepthCm = p.DepthCm
        }).ToList();
    }

    private void ValidateShippingForm(AdminProductFormViewModel vm)
    {
        if (vm.ShippingParcels.Count > ProductShippingRules.MaxParcels)
        {
            ModelState.AddModelError(nameof(vm.ShippingParcels), $"Един продукт може да има до {ProductShippingRules.MaxParcels} колета.");
            return;
        }
        var candidate = new Product { WidthCm = vm.WidthCm, HeightCm = vm.HeightCm, DepthCm = vm.DepthCm };
        ApplyShippingForm(candidate, vm);
        foreach (var error in ProductShippingRules.Validate(candidate))
            ModelState.AddModelError(error.Field, error.Message);
    }

    private static void ApplyShippingForm(Product product, AdminProductFormViewModel vm)
    {
        product.AllowsCourier = vm.AllowsCourier;
        product.UseCatalogDimensionsForShipping = vm.UseCatalogDimensionsForShipping;
        // Parcel rows have no externally posted identity. Keep the existing row for
        // each position, remove only surplus rows, and never attach foreign rows.
        var existing = product.ShippingParcels.ToDictionary(p => p.Position);
        foreach (var removed in product.ShippingParcels.Where(p => p.Position > vm.ShippingParcels.Count).ToList())
            product.ShippingParcels.Remove(removed);
        for (var i = 0; i < vm.ShippingParcels.Count; i++)
        {
            var input = vm.ShippingParcels[i];
            if (!existing.TryGetValue(i + 1, out var parcel))
            {
                parcel = new ProductShippingParcel { Position = i + 1 };
                product.ShippingParcels.Add(parcel);
            }
            parcel.WeightKg = input.WeightKg ?? 0m;
            parcel.WidthCm = input.WidthCm;
            parcel.HeightCm = input.HeightCm;
            parcel.DepthCm = input.DepthCm;
        }
    }
}
