using Algara.Data.Models;
using Algara.Web.Services;
using Algara.Web.ViewModels.Admin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Algara.Web.Controllers;

public partial class AdminController
{
    [HttpGet("fabrics")]
    public async Task<IActionResult> Fabrics(string? search = null)
    {
        search = search?.Trim();
        var query = _shopDb.Fabrics.AsNoTracking().AsQueryable();
        if (!string.IsNullOrEmpty(search))
            query = query.Where(f => f.Code.Contains(search) || f.Name.Contains(search) ||
                (f.EnglishName != null && f.EnglishName.Contains(search)));
        return View(new FabricCatalogViewModel
        {
            Search = search ?? string.Empty,
            Fabrics = await query.Include(f => f.Colors).OrderBy(f => f.Name).ToListAsync()
        });
    }

    [HttpGet("fabrics/create")]
    public IActionResult FabricCreate() => View("FabricEdit", new FabricFormViewModel());

    [HttpPost("fabrics/create"), ValidateAntiForgeryToken]
    [RequestSizeLimit(6 * 1024 * 1024), RequestFormLimits(MultipartBodyLengthLimit = 6 * 1024 * 1024)]
    public Task<IActionResult> FabricCreate(FabricFormViewModel form)
    {
        // The route, not a posted identity, determines whether a row is created.
        form.N = null;
        form.ImageUrl = null;
        return SaveFabricAsync(form, null);
    }

    [HttpGet("fabrics/{n:int}/edit")]
    public async Task<IActionResult> FabricEdit(int n)
    {
        var fabric = await _shopDb.Fabrics.AsNoTracking().SingleOrDefaultAsync(f => f.N == n);
        if (fabric is null) return NotFound();
        return View(new FabricFormViewModel
        {
            N = fabric.N, Code = fabric.Code, Name = fabric.Name, EnglishName = fabric.EnglishName,
            Description = fabric.Description, EnglishDescription = fabric.EnglishDescription,
            ImageUrl = fabric.ImageUrl, IsActive = fabric.IsActive
        });
    }

    [HttpPost("fabrics/{n:int}/edit"), ValidateAntiForgeryToken]
    [RequestSizeLimit(6 * 1024 * 1024), RequestFormLimits(MultipartBodyLengthLimit = 6 * 1024 * 1024)]
    public async Task<IActionResult> FabricEdit(int n, FabricFormViewModel form)
    {
        if (form.N != n) return BadRequest();
        var fabric = await _shopDb.Fabrics.SingleOrDefaultAsync(f => f.N == n);
        if (fabric is null) return NotFound();
        form.ImageUrl = fabric.ImageUrl;
        return await SaveFabricAsync(form, fabric);
    }

    private async Task<IActionResult> SaveFabricAsync(FabricFormViewModel form, Fabric? fabric)
    {
        form.Code = string.IsNullOrWhiteSpace(form.Code) ? CatalogCode.FromName(form.Name) : CatalogCode.Normalize(form.Code);
        if (!CatalogCode.IsValid(form.Code, CatalogCode.FabricMaxLength))
            ModelState.AddModelError(nameof(form.Code), "Кодът трябва да съдържа до 200 видими знака. Може да използвате кирилица.");
        var comparisonCode = form.Code.ToUpperInvariant();
        if (await _shopDb.Fabrics.AnyAsync(f => f.Code.ToUpper() == comparisonCode && f.N != (form.N ?? 0)))
            ModelState.AddModelError(nameof(form.Code), "Вече има дамаска с този код.");
        if (!ModelState.IsValid) return View("FabricEdit", form);

        var storage = new ProductImageStorage(_env);
        IReadOnlyList<string> newFiles = Array.Empty<string>();
        try
        {
            if (form.ImageFile is not null)
            {
                var prepared = await storage.PrepareAsync([form.ImageFile], HttpContext.RequestAborted);
                newFiles = await storage.SaveAsync(prepared, HttpContext.RequestAborted);
            }
            var isNew = fabric is null;
            fabric ??= new Fabric();
            fabric.Code = form.Code;
            fabric.Name = form.Name.Trim();
            fabric.EnglishName = OptionText(form.EnglishName);
            fabric.Description = OptionText(form.Description);
            fabric.EnglishDescription = OptionText(form.EnglishDescription);
            fabric.ImageUrl = newFiles.FirstOrDefault() ?? form.ImageUrl;
            fabric.IsActive = form.IsActive;
            if (isNew) _shopDb.Fabrics.Add(fabric);
            await _shopDb.SaveChangesAsync(HttpContext.RequestAborted);
            // Replaced, committed images may still be used by other catalog records.
            TempData["Success"] = isNew ? "Дамаската е създадена. Добавете нейните цветове." : "Дамаската е записана.";
            return RedirectToAction(nameof(Colors), new { fabricN = fabric.N });
        }
        catch (ProductImageValidationException ex)
        {
            ModelState.AddModelError(nameof(form.ImageFile), ex.Message);
        }
        catch (Exception ex) when (ex is DbUpdateException or IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Fabric catalog save failed.");
            ModelState.AddModelError(OptionDuplicateCode(ex) ? nameof(form.Code) : string.Empty,
                OptionDuplicateCode(ex) ? "Вече има дамаска с този код." : "Дамаската не беше записана. Опитайте отново.");
        }
        catch (OperationCanceledException)
        {
            await RollbackOptionUploadAsync(storage, newFiles);
            throw;
        }
        await RollbackOptionUploadAsync(storage, newFiles);
        return View("FabricEdit", form);
    }

    [HttpGet("colors")]
    public async Task<IActionResult> Colors(string? search = null, int? fabricN = null)
    {
        search = search?.Trim();
        var query = _shopDb.CatalogColors.AsNoTracking().AsQueryable();
        if (fabricN.HasValue)
            query = fabricN == 0 ? query.Where(c => c.FabricN == null) : query.Where(c => c.FabricN == fabricN);
        if (!string.IsNullOrEmpty(search))
            query = query.Where(c => c.Code.Contains(search) || c.Name.Contains(search) ||
                (c.EnglishName != null && c.EnglishName.Contains(search)));
        return View(new ColorCatalogViewModel
        {
            Search = search ?? string.Empty,
            FabricN = fabricN,
            Colors = await query.Include(c => c.Fabric).OrderBy(c => c.FabricN).ThenBy(c => c.Name).ToListAsync(),
            Fabrics = await OptionFabricChoicesAsync()
        });
    }

    [HttpGet("colors/create")]
    public async Task<IActionResult> ColorCreate(int? fabricN = null)
    {
        if (fabricN == 0) fabricN = null;
        if (fabricN.HasValue && !await _shopDb.Fabrics.AnyAsync(f => f.N == fabricN)) return NotFound();
        var form = new ColorFormViewModel { FabricN = fabricN };
        await PopulateColorFormAsync(form);
        return View("ColorEdit", form);
    }

    [HttpPost("colors/create"), ValidateAntiForgeryToken]
    [RequestSizeLimit(6 * 1024 * 1024), RequestFormLimits(MultipartBodyLengthLimit = 6 * 1024 * 1024)]
    public Task<IActionResult> ColorCreate(ColorFormViewModel form)
    {
        form.N = null;
        form.ImageUrl = null;
        return SaveColorAsync(form, null);
    }

    [HttpGet("colors/{n:int}/edit")]
    public async Task<IActionResult> ColorEdit(int n)
    {
        var color = await _shopDb.CatalogColors.AsNoTracking().SingleOrDefaultAsync(c => c.N == n);
        if (color is null) return NotFound();
        var form = new ColorFormViewModel
        {
            N = color.N, FabricN = color.FabricN, Code = color.Code, Name = color.Name,
            EnglishName = color.EnglishName, HexColor = color.HexColor,
            ImageUrl = color.ImageUrl, IsActive = color.IsActive
        };
        await PopulateColorFormAsync(form);
        return View(form);
    }

    [HttpPost("colors/{n:int}/edit"), ValidateAntiForgeryToken]
    [RequestSizeLimit(6 * 1024 * 1024), RequestFormLimits(MultipartBodyLengthLimit = 6 * 1024 * 1024)]
    public async Task<IActionResult> ColorEdit(int n, ColorFormViewModel form)
    {
        if (form.N != n) return BadRequest();
        var color = await _shopDb.CatalogColors.SingleOrDefaultAsync(c => c.N == n);
        if (color is null) return NotFound();
        if (color.FabricN != form.FabricN) return BadRequest();
        // Neither a posted URL nor another collection can replace saved catalog membership.
        form.ImageUrl = color.ImageUrl;
        return await SaveColorAsync(form, color);
    }

    private async Task<IActionResult> SaveColorAsync(ColorFormViewModel form, CatalogColor? color)
    {
        form.Code = CatalogCode.Normalize(form.Code);
        if (!CatalogCode.IsValid(form.Code))
            ModelState.AddModelError(nameof(form.Code), "Кодът трябва да съдържа до 64 видими знака. Може да използвате кирилица.");
        if (form.FabricN.HasValue && !await _shopDb.Fabrics.AnyAsync(f => f.N == form.FabricN))
            ModelState.AddModelError(nameof(form.FabricN), "Изберете съществуваща дамаска.");
        var comparisonCode = form.Code.ToUpperInvariant();
        if (await _shopDb.CatalogColors.AnyAsync(c => c.FabricN == form.FabricN && c.Code.ToUpper() == comparisonCode && c.N != (form.N ?? 0)))
            ModelState.AddModelError(nameof(form.Code), "Вече има цвят с този код в избраната дамаска или в самостоятелните цветове.");
        if (string.IsNullOrWhiteSpace(form.HexColor) && string.IsNullOrWhiteSpace(form.ImageUrl) && form.SwatchFile is null)
            ModelState.AddModelError(nameof(form.SwatchFile), "Качете мостра или изберете цвят от палитрата.");
        if (!ModelState.IsValid)
        {
            await PopulateColorFormAsync(form);
            return View("ColorEdit", form);
        }

        var storage = new ProductImageStorage(_env);
        IReadOnlyList<string> newFiles = Array.Empty<string>();
        try
        {
            if (form.SwatchFile is not null)
            {
                var prepared = await storage.PrepareAsync([form.SwatchFile], HttpContext.RequestAborted);
                newFiles = await storage.SaveAsync(prepared, HttpContext.RequestAborted);
            }
            var isNew = color is null;
            color ??= new CatalogColor();
            color.FabricN = form.FabricN;
            color.Code = form.Code;
            color.Name = form.Name.Trim();
            color.EnglishName = OptionText(form.EnglishName);
            color.HexColor = OptionText(form.HexColor)?.ToUpperInvariant();
            color.ImageUrl = newFiles.FirstOrDefault() ?? form.ImageUrl;
            color.IsActive = form.IsActive;
            if (isNew) _shopDb.CatalogColors.Add(color);
            await _shopDb.SaveChangesAsync(HttpContext.RequestAborted);
            // Previously committed images can still be referenced elsewhere; retain them.
            TempData["Success"] = "Цветът е записан.";
            return RedirectToAction(nameof(Colors), new { fabricN = color.FabricN ?? 0 });
        }
        catch (ProductImageValidationException ex)
        {
            ModelState.AddModelError(nameof(form.SwatchFile), ex.Message);
        }
        catch (Exception ex) when (ex is DbUpdateException or IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Color catalog save failed.");
            ModelState.AddModelError(OptionDuplicateCode(ex) ? nameof(form.Code) : string.Empty,
                OptionDuplicateCode(ex) ? "Вече има цвят с този код в избраната колекция." : "Цветът не беше записан. Опитайте отново.");
        }
        catch (OperationCanceledException)
        {
            await RollbackOptionUploadAsync(storage, newFiles);
            throw;
        }
        await RollbackOptionUploadAsync(storage, newFiles);
        await PopulateColorFormAsync(form);
        return View("ColorEdit", form);
    }

    private async Task RollbackOptionUploadAsync(ProductImageStorage storage, IReadOnlyList<string> newFiles)
    {
        try { await storage.DeleteNewFilesAsync(newFiles); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not remove newly uploaded swatch after failed catalog save.");
        }
    }

    private async Task PopulateColorFormAsync(ColorFormViewModel form)
    {
        form.Fabrics = await OptionFabricChoicesAsync();
        form.FabricName = form.FabricN.HasValue
            ? form.Fabrics.FirstOrDefault(f => f.Value == form.FabricN.Value.ToString())?.Text
            : "Самостоятелен цвят";
    }

    private async Task<List<SelectListItem>> OptionFabricChoicesAsync()
        => await _shopDb.Fabrics.AsNoTracking().OrderBy(f => f.Name)
            .Select(f => new SelectListItem { Value = f.N.ToString(), Text = f.Name + (f.IsActive ? "" : " (неактивна)") }).ToListAsync();

    private static string? OptionText(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool OptionDuplicateCode(Exception ex)
        => ex is DbUpdateException { InnerException: SqlException { Number: 2601 or 2627 } };
}
