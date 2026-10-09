using Algara.Data.Models;
using Algara.Data.Repositories;
using Algara.Web.Services;
using Algara.Web.ViewModels.Admin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Algara.Web.Controllers;

public partial class AdminController
{
    private const int MaxProductImages = 20;
    private const long ProductUploadLimit = 110L * 1024 * 1024;

    [HttpGet("products/create")]
    public async Task<IActionResult> ProductCreate()
        => View(await PrepareProductFormAsync(new AdminProductFormViewModel()));

    [HttpPost("products/create")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(ProductUploadLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = ProductUploadLimit)]
    public async Task<IActionResult> ProductCreate(AdminProductFormViewModel vm)
        => await SaveProductAsync(vm, null);

    [HttpGet("products/edit/{n}")]
    public async Task<IActionResult> ProductEdit(int n)
    {
        var product = await LoadEditableProductAsync(n);
        if (product == null) return NotFound();
        var english = product.Translations.FirstOrDefault(t => t.Language == "en");
        var vm = new AdminProductFormViewModel
        {
            N = product.N, Sku = product.Sku, Name = product.Name,
            Description = product.Description, Materials = product.Materials,
            EnglishName = english?.Name, EnglishDescription = english?.Description,
            EnglishMaterials = english?.Materials,
            WidthCm = product.WidthCm, HeightCm = product.HeightCm, DepthCm = product.DepthCm,
            Price = product.Price, IsCustomizable = product.IsCustomizable,
            IsFeatured = product.IsFeatured, IsActive = product.IsActive, CategoryN = product.CategoryN,
            SelectedSubCategoryNs = product.ProductSubCategories.Select(s => s.SubCategoryN).ToList(),
            Images = ImageRows(product)
        };
        return View(await PrepareProductFormAsync(vm, product));
    }

    [HttpPost("products/edit/{n}")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(ProductUploadLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = ProductUploadLimit)]
    public async Task<IActionResult> ProductEdit(AdminProductFormViewModel vm)
    {
        // Do not let a modified hidden field silently target a different route.
        if (RouteData.Values.TryGetValue("n", out var routeN) &&
            (!int.TryParse(routeN?.ToString(), out var n) || n != vm.N))
            return BadRequest();
        await using var write = await GalleryWriteLock.AcquireAsync(vm.N, HttpContext.RequestAborted);
        var product = await LoadEditableProductAsync(vm.N);
        if (product == null) return NotFound();
        var result = await SaveProductAsync(vm, product);
        if (result is RedirectToActionResult) await write.CommitAsync(HttpContext.RequestAborted);
        return result;
    }

    private Task<Product?> LoadEditableProductAsync(int n)
        => _shopDb.Products.Include(p => p.ProductSubCategories)
            .Include(p => p.Translations).Include(p => p.Images)
            .AsSplitQuery().FirstOrDefaultAsync(p => p.N == n);

    private async Task<IActionResult> SaveProductAsync(AdminProductFormViewModel vm, Product? existing)
    {
        vm.Images ??= [];
        vm.SelectedSubCategoryNs ??= [];
        vm.Sku = ProductSku.Normalize(vm.Sku);
        vm.Name = vm.Name?.Trim() ?? string.Empty;
        var originalImages = existing == null ? [] : ImageRows(existing);
        ProductImageDraftStorage.DraftLease? draft = null;
        var storage = new ProductImageStorage(_env);
        IReadOnlyList<string> savedUrls = [];
        var saved = false;
        try
        {
            if (existing == null && !string.IsNullOrEmpty(vm.GalleryDraftId))
            {
                draft = await DraftStorage.OpenAsync(vm.GalleryDraftId, HttpContext.RequestAborted);
                originalImages = DraftRows(vm.GalleryDraftId, draft.Images);
            }
            await ValidateProductFormAsync(vm, existing);
            ValidateGallery(vm, originalImages);
            if (!ModelState.IsValid)
            {
                RestoreImageRows(vm, originalImages);
                return View(await PrepareProductFormAsync(vm));
            }
            if (draft != null)
            {
                var prepared = await draft.PrepareAsync(vm.Images.OrderBy(i => i.DisplayOrder).Select(i => i.UploadId!), HttpContext.RequestAborted);
                savedUrls = await storage.SaveAsync(prepared, HttpContext.RequestAborted);
            }
            var product = existing ?? new Product();
            ApplyProductForm(product, vm, savedUrls);
            if (existing == null) await _productRepo.AddAsync(product);
            else await _productRepo.UpdateAsync(product);
            saved = true;
            if (draft != null)
            {
                try { await draft.CompleteAsync(); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { _logger.LogWarning(ex, "Could not finish image draft cleanup for product {ProductN}", product.N); }
            }
            TempData["Success"] = $"Продуктът \"{product.Name}\" е записан успешно. Код: {product.Sku}.";
            return RedirectToAction(nameof(Products));
        }
        catch (ProductImageValidationException ex)
        {
            ModelState.AddModelError(nameof(vm.Images), ex.Message);
        }
        catch (ProductSkuConflictException)
        {
            ModelState.AddModelError(nameof(vm.Sku), "Този артикулен код вече се използва. Въведете друг код.");
        }
        catch (Exception ex) when (ex is DbUpdateException or IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Could not save product {ProductN}", existing?.N);
            ModelState.AddModelError(string.Empty, "Продуктът не беше записан. Опитайте отново.");
        }
        finally
        {
            if (!saved) await storage.DeleteNewFilesAsync(savedUrls);
            if (draft != null) await draft.DisposeAsync();
        }
        RestoreImageRows(vm, originalImages);
        return View(await PrepareProductFormAsync(vm));
    }

    private void ValidateGallery(AdminProductFormViewModel vm, List<AdminProductImageViewModel> current)
    {
        var expected = current.Select(ImageKey).ToHashSet(StringComparer.Ordinal);
        var posted = vm.Images.Select(ImageKey).ToList();
        if (posted.Count != expected.Count || posted.Distinct(StringComparer.Ordinal).Count() != posted.Count ||
            posted.Any(id => !expected.Contains(id)) ||
            vm.Images.Any(i => i.DisplayOrder < 0 || i.DisplayOrder > 10000) ||
            vm.Images.Select(i => i.DisplayOrder).Distinct().Count() != vm.Images.Count)
            ModelState.AddModelError(nameof(vm.Images), "Галерията е променена. Проверете подредбата на показаните снимки и запишете отново.");
        if (current.Count > MaxProductImages)
            ModelState.AddModelError(nameof(vm.Images), $"Галерията може да съдържа най-много {MaxProductImages} снимки.");
    }

    private async Task ValidateProductFormAsync(AdminProductFormViewModel vm, Product? existing)
    {
        if (string.IsNullOrWhiteSpace(vm.Name))
            ModelState.AddModelError(nameof(vm.Name), "Името е задължително.");
        if (!string.IsNullOrEmpty(vm.Sku) && !ProductSku.IsValid(vm.Sku))
            ModelState.AddModelError(nameof(vm.Sku), "Използвайте до 64 латински букви, цифри, тире или долна черта.");
        else if (!string.IsNullOrEmpty(vm.Sku) &&
            await _shopDb.Products.AnyAsync(p => p.Sku == vm.Sku && p.N != (existing == null ? 0 : existing.N)))
            ModelState.AddModelError(nameof(vm.Sku), "Този артикулен код вече се използва. Въведете друг код.");

        if (vm.CategoryN.HasValue && !await _shopDb.Categories.AnyAsync(c => c.N == vm.CategoryN &&
            (c.IsActive || existing != null && c.N == existing.CategoryN)))
            ModelState.AddModelError(nameof(vm.CategoryN), "Изберете съществуваща активна категория.");
        var selected = vm.SelectedSubCategoryNs.Distinct().ToList();
        if (selected.Count > 0)
        {
            var allowed = await _shopDb.SubCategories.Where(s => selected.Contains(s.N) && s.CategoryN == vm.CategoryN)
                .Select(s => new { s.N, s.IsActive }).ToListAsync();
            var previous = existing?.ProductSubCategories.Select(s => s.SubCategoryN).ToHashSet() ?? [];
            if (allowed.Count != selected.Count || allowed.Any(s => !s.IsActive && !previous.Contains(s.N)))
                ModelState.AddModelError(nameof(vm.SelectedSubCategoryNs), "Подкатегориите трябва да принадлежат към избраната категория.");
        }
        vm.SelectedSubCategoryNs = selected;
    }

    private static void ApplyProductForm(Product product, AdminProductFormViewModel vm, IReadOnlyList<string> uploaded)
    {
        // An empty code on edit preserves the existing code; new products allocate one.
        if (!string.IsNullOrEmpty(vm.Sku) || product.N == 0) product.Sku = vm.Sku ?? string.Empty;
        product.Name = vm.Name;
        product.Description = EmptyToNull(vm.Description);
        product.Materials = EmptyToNull(vm.Materials);
        product.WidthCm = vm.WidthCm;
        product.HeightCm = vm.HeightCm;
        product.DepthCm = vm.DepthCm;
        product.Price = vm.Price;
        product.IsCustomizable = vm.IsCustomizable;
        product.IsFeatured = vm.IsFeatured;
        product.IsActive = vm.IsActive;
        product.CategoryN = vm.CategoryN;
        var english = product.Translations.FirstOrDefault(t => t.Language == "en");
        var name = EmptyToNull(vm.EnglishName);
        var description = EmptyToNull(vm.EnglishDescription);
        var materials = EmptyToNull(vm.EnglishMaterials);
        if (name != null || description != null || materials != null)
        {
            if (english == null)
            {
                english = new ProductTranslation { Language = "en" };
                product.Translations.Add(english);
            }
            english.Name = name;
            english.Description = description;
            english.Materials = materials;
        }
        else if (english != null) product.Translations.Remove(english);

        var ordered = product.N == 0
            ? uploaded.Select(url => new ProductImage { ImageUrl = url }).ToList()
            : vm.Images.OrderBy(i => i.DisplayOrder).Select(row => product.Images.Single(i => i.N == row.N)).ToList();
        if (product.N == 0) foreach (var image in ordered) product.Images.Add(image);
        for (var i = 0; i < ordered.Count; i++) ordered[i].DisplayOrder = i;
        product.ImageUrl = ordered.FirstOrDefault()?.ImageUrl;

        var selected = vm.SelectedSubCategoryNs.ToHashSet();
        foreach (var removed in product.ProductSubCategories.Where(s => !selected.Contains(s.SubCategoryN)).ToList())
            product.ProductSubCategories.Remove(removed);
        var assigned = product.ProductSubCategories.Select(s => s.SubCategoryN).ToHashSet();
        foreach (var n in selected.Except(assigned))
            product.ProductSubCategories.Add(new ProductSubCategory { SubCategoryN = n });
    }

    private async Task<AdminProductFormViewModel> PrepareProductFormAsync(AdminProductFormViewModel vm, Product? product = null)
    {
        vm.Categories = await _shopDb.Categories.Where(c => c.IsActive || c.N == vm.CategoryN)
            .OrderBy(c => c.Name).ToListAsync();
        if (vm.CategoryN.HasValue)
            vm.AvailableSubCategories = await _shopDb.SubCategories
                .Where(s => s.CategoryN == vm.CategoryN && (s.IsActive || vm.SelectedSubCategoryNs.Contains(s.N)))
                .OrderBy(s => s.Name).ToListAsync();
        if (product != null) RestoreImageRows(vm, ImageRows(product));
        if (vm.N == 0 && string.IsNullOrEmpty(vm.GalleryDraftId))
            vm.GalleryDraftId = await DraftStorage.CreateAsync(HttpContext.RequestAborted);
        return vm;
    }

    private static List<AdminProductImageViewModel> ImageRows(Product product)
        => product.Images.OrderBy(i => i.DisplayOrder).ThenBy(i => i.N)
            .Select(i => new AdminProductImageViewModel { N = i.N, ImageUrl = i.ImageUrl, DisplayOrder = i.DisplayOrder }).ToList();

    private void RestoreImageRows(AdminProductFormViewModel vm, List<AdminProductImageViewModel> original)
    {
        var posted = vm.Images.GroupBy(ImageKey).ToDictionary(g => g.Key, g => g.First());
        foreach (var row in original)
            if (posted.TryGetValue(ImageKey(row), out var input)) row.DisplayOrder = input.DisplayOrder;
        vm.Images = original.OrderBy(i => i.DisplayOrder).ThenBy(i => i.N).ToList();
        for (var i = 0; i < vm.Images.Count; i++) vm.Images[i].DisplayOrder = i;
        foreach (var key in ModelState.Keys.Where(k => k.StartsWith("Images[", StringComparison.Ordinal)).ToArray())
            ModelState.Remove(key);
    }

    private static string ImageKey(AdminProductImageViewModel image)
        => image.N > 0 && string.IsNullOrEmpty(image.UploadId) ? $"saved:{image.N}" : image.N == 0 ? $"draft:{image.UploadId}" : "invalid";

    private static string? EmptyToNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
