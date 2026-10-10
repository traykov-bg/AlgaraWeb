using Algara.Data.Models;
using Algara.Web.ViewModels.Admin;
using Microsoft.EntityFrameworkCore;

namespace Algara.Web.Controllers;

public partial class AdminController
{
    private static bool MatchesProductVersion(Product product, string? version)
        => product.RowVersion.Length == 0 || string.Equals(Convert.ToBase64String(product.RowVersion), version, StringComparison.Ordinal);

    private static void ReadVariantForm(AdminProductFormViewModel vm, Product product)
    {
        vm.RowVersion = Convert.ToBase64String(product.RowVersion);
        vm.OptionKind = product.OptionKind;
        vm.StockQuantity = product.StockQuantity;
        vm.Variants = product.Variants.OrderByDescending(v => v.IsActive).ThenBy(v => v.N).Select(v => new AdminProductVariantViewModel
        {
            N = v.N, CatalogColorN = v.CatalogColorN, Sku = v.Sku, StockQuantity = v.StockQuantity,
            Surcharge = v.Surcharge, ProductImageN = v.ProductImageN, IsDefault = v.IsDefault, IsActive = v.IsActive
        }).ToList();
        vm.FabricSurcharges = product.FabricSurcharges.Select(s => new AdminProductFabricSurchargeViewModel
        { FabricN = s.FabricN, Surcharge = s.Surcharge }).ToList();
    }

    private static void RestoreRetainedVariantRows(AdminProductFormViewModel vm, Product? existing)
    {
        if (existing == null) return;
        // A failed form still needs the identities of omitted groups so adding one
        // back cannot accidentally create duplicate variants. Group membership
        // remains the submitted surcharge list; these cached rows stay inactive.
        var submitted = vm.Variants.Select(v => v.N).ToHashSet();
        foreach (var old in existing.Variants.Where(v => !submitted.Contains(v.N)))
            vm.Variants.Add(new AdminProductVariantViewModel
            {
                N = old.N, CatalogColorN = old.CatalogColorN, Sku = old.Sku,
                StockQuantity = old.StockQuantity, Surcharge = old.Surcharge,
                ProductImageN = old.ProductImageN, IsActive = false, IsDefault = false
            });
    }
    private async Task PrepareVariantFormAsync(AdminProductFormViewModel vm)
    {
        vm.AvailableFabrics = await _shopDb.Fabrics.OrderBy(f => f.Name).ToListAsync();
        vm.AvailableColors = await _shopDb.CatalogColors.Include(c => c.Fabric).OrderBy(c => c.Name).ToListAsync();
    }

    private async Task ValidateProductVariantsAsync(AdminProductFormViewModel vm, Product? existing)
    {
        vm.Variants ??= [];
        vm.FabricSurcharges ??= [];
        if (existing != null && !MatchesProductVersion(existing, vm.RowVersion))
            ModelState.AddModelError(string.Empty, "Продуктът е променен в друга редакция. Презаредете страницата преди запис, за да не загубите новите данни.");
        if (!Enum.IsDefined(vm.OptionKind))
            ModelState.AddModelError(nameof(vm.OptionKind), "Изберете валиден вид варианти.");
        if (vm.StockQuantity is < 0 or > 1_000_000)
            ModelState.AddModelError(nameof(vm.StockQuantity), "Въведете брой от 0 до 1 000 000 или оставете празно при неизвестна наличност.");
        if (vm.Variants.Count > 500 || vm.FabricSurcharges.Count > 500)
        {
            ModelState.AddModelError(nameof(vm.Variants), "Една редакция поддържа до 500 варианта.");
            return;
        }
        var oldVariants = existing?.Variants.ToDictionary(v => v.N) ?? [];
        var ids = vm.Variants.Select(v => v.CatalogColorN).Distinct().ToList();
        var colors = await _shopDb.CatalogColors.Include(c => c.Fabric).Where(c => ids.Contains(c.N)).ToDictionaryAsync(c => c.N);
        var fabricIds = vm.FabricSurcharges.Select(s => s.FabricN).ToList();
        var fabrics = await _shopDb.Fabrics.Where(f => fabricIds.Contains(f.N)).ToDictionaryAsync(f => f.N);
        if (ids.Count != vm.Variants.Count || vm.Variants.Where(v => v.N > 0).Select(v => v.N).Distinct().Count() != vm.Variants.Count(v => v.N > 0))
            ModelState.AddModelError(nameof(vm.Variants), "Всеки вариант може да присъства само веднъж.");
        if (fabricIds.Distinct().Count() != fabricIds.Count)
            ModelState.AddModelError(nameof(vm.FabricSurcharges), "Всяка дамаска може да има само една надценка.");
        foreach (var surcharge in vm.FabricSurcharges)
        {
            if (!fabrics.ContainsKey(surcharge.FabricN) || !ValidSurcharge(surcharge.Surcharge))
                ModelState.AddModelError(nameof(vm.FabricSurcharges), "Изберете съществуваща дамаска и надценка от 0 до 9 999 999 евро с най-много два знака след десетичната запетая.");
        }
        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < vm.Variants.Count; i++)
        {
            var row = vm.Variants[i];
            var key = $"Variants[{i}]";
            oldVariants.TryGetValue(row.N, out var previous);
            if (row.N < 0 || row.N > 0 && (previous == null || previous.CatalogColorN != row.CatalogColorN))
                ModelState.AddModelError(key + ".N", "Вариантът не принадлежи към този продукт или изборът му е променен. Добавете отделен вариант.");
            if (row.N == 0 && oldVariants.Values.Any(v => v.CatalogColorN == row.CatalogColorN))
                ModelState.AddModelError(key + ".N", "Този вариант вече съществува. Включете съществуващия ред.");
            row.Sku = ProductSku.Normalize(row.Sku);
            if (row.Sku.Length == 0 && previous != null) row.Sku = previous.Sku;
            if (row.Sku.Length > 0 && (!ProductSku.IsValid(row.Sku) || !codes.Add(row.Sku) || row.Sku == vm.Sku))
                ModelState.AddModelError(key + ".Sku", "Въведете различен валиден артикулен код за всеки вариант.");
            if (row.StockQuantity is < 0 or > 1_000_000)
                ModelState.AddModelError(key + ".StockQuantity", "Наличността трябва да е брой от 0 до 1 000 000 или празно поле.");
            if (!ValidSurcharge(row.Surcharge))
                ModelState.AddModelError(key + ".Surcharge", "Надценката трябва да е от 0 до 9 999 999 евро, с най-много два десетични знака.");
            if (!colors.TryGetValue(row.CatalogColorN, out var color))
            {
                ModelState.AddModelError(key + ".CatalogColorN", "Изберете съществуващ цвят.");
                continue;
            }
            if (row.IsActive)
            {
                if (!color.IsActive || color.FabricN.HasValue && color.Fabric?.IsActive != true)
                    ModelState.AddModelError(key + ".CatalogColorN", "Активирайте цвета и дамаската в картотеката, преди да включите варианта.");
                if (vm.OptionKind == ProductOptionKind.None || (vm.OptionKind == ProductOptionKind.Fabric) != color.FabricN.HasValue)
                    ModelState.AddModelError(key + ".CatalogColorN", "Цветът не съответства на избрания вид варианти.");
                if (vm.OptionKind == ProductOptionKind.Fabric && (color.FabricN is not int fabricN || !fabricIds.Contains(fabricN) || row.Surcharge != 0))
                    ModelState.AddModelError(key + ".Surcharge", "За дамаски задайте една обща надценка; цветовете нямат отделна надценка.");
            }
            if (row.ProductImageN.HasValue && (row.ProductImageN <= 0 || existing == null || !string.IsNullOrEmpty(row.ProductImageUploadId) || !vm.Images.Any(x => x.N == row.ProductImageN)))
                ModelState.AddModelError(key + ".ProductImageN", "Снимката трябва да е от галерията на този продукт.");
            if (!string.IsNullOrEmpty(row.ProductImageUploadId) && (existing != null || !vm.Images.Any(x => x.UploadId == row.ProductImageUploadId)))
                ModelState.AddModelError(key + ".ProductImageUploadId", "Избраната временна снимка липсва от галерията.");
        }
        var skuRows = vm.Variants.Where(v => !string.IsNullOrEmpty(v.Sku)).ToList();
        if (skuRows.Count > 0)
        {
            var wanted = skuRows.Select(v => v.Sku!).ToList();
            var used = await _shopDb.ProductVariants.Where(v => wanted.Contains(v.Sku)).Select(v => new { v.N, v.Sku }).ToListAsync();
            var productCodes = await _shopDb.Products.Where(p => wanted.Contains(p.Sku)).Select(p => p.Sku).ToListAsync();
            if (skuRows.Any(v => used.Any(u => u.Sku.Equals(v.Sku, StringComparison.OrdinalIgnoreCase) && u.N != v.N)) || productCodes.Count > 0)
                ModelState.AddModelError(nameof(vm.Variants), "Артикулен код на вариант вече се използва.");
        }
        var active = vm.Variants.Where(v => v.IsActive).ToList();
        if (vm.OptionKind != ProductOptionKind.None)
        {
            if (active.Count == 0 || active.Count(v => v.IsDefault) != 1)
                ModelState.AddModelError(nameof(vm.Variants), "Изберете поне един активен вариант и точно един основен вариант.");
            var defaultRow = active.FirstOrDefault(v => v.IsDefault);
            if (defaultRow != null && colors.TryGetValue(defaultRow.CatalogColorN, out var color))
            {
                var amount = vm.OptionKind == ProductOptionKind.Fabric
                    ? vm.FabricSurcharges.FirstOrDefault(s => s.FabricN == color.FabricN)?.Surcharge
                    : defaultRow.Surcharge;
                if (amount != 0)
                    ModelState.AddModelError(nameof(vm.Variants), "Основният вариант трябва да е включен в базовата цена (надценка 0 €).");
            }
        }
        else if (active.Count > 0)
            ModelState.AddModelError(nameof(vm.Variants), "При продукт без варианти изключете активните варианти.");
    }

    private static bool ValidSurcharge(decimal value) => value >= 0 && value <= 9_999_999 && decimal.Round(value, 2) == value;

    private static void ApplyVariantForm(Product product, AdminProductFormViewModel vm)
    {
        // Fabric selection supplies the manufacturing classification. Preserve an
        // existing non-fabric classification until the option kind is changed.
        product.IsUpholstered = vm.OptionKind == ProductOptionKind.Fabric ||
            product.N > 0 && product.OptionKind == vm.OptionKind && product.IsUpholstered;
        product.OptionKind = vm.OptionKind;
        product.StockQuantity = vm.OptionKind == ProductOptionKind.None ? vm.StockQuantity : null;
        // Keep stable variant identities even when temporarily removed from the selection.
        foreach (var old in product.Variants) { old.IsActive = false; old.IsDefault = false; }
        foreach (var row in vm.Variants)
        {
            var variant = product.Variants.SingleOrDefault(v => row.N > 0 && v.N == row.N);
            if (variant == null)
            {
                variant = new ProductVariant { CatalogColorN = row.CatalogColorN };
                product.Variants.Add(variant);
            }
            variant.Sku = row.Sku ?? string.Empty;
            variant.StockQuantity = row.StockQuantity;
            variant.Surcharge = vm.OptionKind == ProductOptionKind.Fabric && row.IsActive ? 0 : row.Surcharge;
            variant.IsActive = row.IsActive;
            variant.IsDefault = row.IsActive && row.IsDefault;
            variant.ProductImageN = row.ProductImageN;
            variant.Image = row.ProductImageN.HasValue ? product.Images.Single(i => i.N == row.ProductImageN) : null;
            if (!string.IsNullOrEmpty(row.ProductImageUploadId))
            {
                var index = vm.Images.OrderBy(i => i.DisplayOrder).ToList().FindIndex(i => i.UploadId == row.ProductImageUploadId);
                variant.Image = product.Images.OrderBy(i => i.DisplayOrder).ElementAt(index);
            }
        }
        var selectedFabrics = vm.FabricSurcharges.Select(s => s.FabricN).ToHashSet();
        foreach (var removed in product.FabricSurcharges.Where(s => !selectedFabrics.Contains(s.FabricN)).ToList())
            product.FabricSurcharges.Remove(removed);
        foreach (var row in vm.FabricSurcharges)
        {
            var surcharge = product.FabricSurcharges.SingleOrDefault(s => s.FabricN == row.FabricN);
            if (surcharge == null) { surcharge = new ProductFabricSurcharge { FabricN = row.FabricN }; product.FabricSurcharges.Add(surcharge); }
            surcharge.Surcharge = row.Surcharge;
        }
    }
}
