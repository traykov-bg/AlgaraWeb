using Algara.Web.Authorization;
using System.Data.Common;
using System.Security.Claims;
using Algara.Data.Models;
using Algara.Web.Services;
using Algara.Web.ViewModels.Admin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Algara.Web.Controllers;

public partial class AdminController
{
    private IProductGalleryWriteLock GalleryWriteLock => HttpContext.RequestServices.GetRequiredService<IProductGalleryWriteLock>();
    private ProductImageDraftStorage DraftStorage => new(_env, User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty);

    [HttpPost("products/images/upload")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(ProductUploadLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = ProductUploadLimit)]
    [RequirePermission("Products.View")]
    public async Task<IActionResult> ProductImagesUpload(int productN, string? draftId, List<IFormFile> files, string? rowVersion = null)
    {
        if (!await CanAsync(productN == 0 ? "Products.Create" : "Products.Edit")) return Forbid();
        var storage = new ProductImageStorage(_env);
        IReadOnlyList<string> savedUrls = [];
        var committed = false;
        try
        {
            if (files == null || files.Count == 0) return GalleryError("Изберете поне една снимка.");
            if (productN < 0) return GalleryError("Невалиден продукт.");
            var prepared = await storage.PrepareAsync(files, HttpContext.RequestAborted);
            if (productN == 0)
            {
                await using var draft = await DraftStorage.OpenAsync(draftId, HttpContext.RequestAborted);
                var ids = await draft.AddAsync(prepared, HttpContext.RequestAborted);
                return Json(new { images = DraftRows(draftId!, ids).Select(i => new { n = 0, i.UploadId, i.ImageUrl }) });
            }

            await using var write = await GalleryWriteLock.AcquireAsync(productN, HttpContext.RequestAborted);
            var product = await LoadEditableProductAsync(productN);
            if (product == null) return GalleryError("Продуктът не е намерен.", 404);
            if (!MatchesProductVersion(product, rowVersion)) return GalleryError("Продуктът е променен. Презаредете страницата преди промяна на снимките.", 409);
            if (product.Images.Count + files.Count > MaxProductImages)
                return GalleryError($"Галерията може да съдържа най-много {MaxProductImages} снимки.");
            savedUrls = await storage.SaveAsync(prepared, HttpContext.RequestAborted);
            var next = product.Images.Count == 0 ? 0 : product.Images.Max(i => i.DisplayOrder) + 1;
            var added = savedUrls.Select(url => new ProductImage { ImageUrl = url, DisplayOrder = next++ }).ToList();
            foreach (var image in added) product.Images.Add(image);
            product.ImageUrl = product.Images.OrderBy(i => i.DisplayOrder).ThenBy(i => i.N).First().ImageUrl;
            await _productRepo.UpdateAsync(product);
            await write.CommitAsync(HttpContext.RequestAborted);
            committed = true;
            return Json(new { rowVersion = Convert.ToBase64String(product.RowVersion), images = added.Select(i => new { n = i.N, uploadId = (string?)null, imageUrl = i.ImageUrl }) });
        }
        catch (ProductImageValidationException ex) { return GalleryError(ex.Message); }
        catch (Exception ex) when (ex is DbUpdateException or DbException or IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Could not upload images for product {ProductN}", productN);
            return GalleryError("Снимките не бяха качени. Опитайте отново.", 500);
        }
        finally { if (!committed) await storage.DeleteNewFilesAsync(savedUrls); }
    }

    [HttpPost("products/images/delete")]
    [ValidateAntiForgeryToken]
    [RequirePermission("Products.View")]
    public async Task<IActionResult> ProductImageDelete(int productN, string? draftId, int imageN, string? uploadId, string? rowVersion = null)
    {
        if (!await CanAsync(productN == 0 ? "Products.Create" : "Products.Edit")) return Forbid();
        try
        {
            if (productN == 0)
            {
                if (imageN != 0) return GalleryError("Невалидна снимка.");
                await using var draft = await DraftStorage.OpenAsync(draftId, HttpContext.RequestAborted);
                await draft.DeleteAsync(uploadId, HttpContext.RequestAborted);
                return Json(new { });
            }
            if (productN < 0 || imageN <= 0 || !string.IsNullOrEmpty(uploadId)) return GalleryError("Невалидна снимка.");
            await using var write = await GalleryWriteLock.AcquireAsync(productN, HttpContext.RequestAborted);
            var product = await LoadEditableProductAsync(productN);
            if (product == null) return GalleryError("Продуктът не е намерен.", 404);
            if (!MatchesProductVersion(product, rowVersion)) return GalleryError("Продуктът е променен. Презаредете страницата преди промяна на снимките.", 409);
            var image = product.Images.SingleOrDefault(i => i.N == imageN);
            if (image == null) return GalleryError("Снимката не е намерена. Презаредете галерията.", 404);
            foreach (var variant in product.Variants.Where(v => v.ProductImageN == imageN))
            {
                variant.Image = null;
                variant.ProductImageN = null;
            }
            product.Images.Remove(image);
            var ordered = product.Images.OrderBy(i => i.DisplayOrder).ThenBy(i => i.N).ToArray();
            for (var i = 0; i < ordered.Length; i++) ordered[i].DisplayOrder = i;
            product.ImageUrl = ordered.FirstOrDefault()?.ImageUrl;
            await _productRepo.UpdateAsync(product);
            await write.CommitAsync(HttpContext.RequestAborted);
            // Never delete arbitrary legacy/shared files from a posted path.
            return Json(new { rowVersion = Convert.ToBase64String(product.RowVersion) });
        }
        catch (ProductImageValidationException ex) { return GalleryError(ex.Message); }
        catch (Exception ex) when (ex is DbUpdateException or DbException or IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Could not delete image {ImageN} for product {ProductN}", imageN, productN);
            return GalleryError("Снимката не беше изтрита. Опитайте отново.", 500);
        }
    }

    [HttpGet("products/images/draft")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [RequirePermission("Products.View", "Products.Create")]
    public async Task<IActionResult> ProductImageDraft(string draftId, string uploadId)
    {
        try
        {
            await using var draft = await DraftStorage.OpenAsync(draftId, HttpContext.RequestAborted);
            var bytes = await draft.ReadAsync(uploadId, HttpContext.RequestAborted);
            var type = Path.GetExtension(uploadId) switch { ".jpg" => "image/jpeg", ".png" => "image/png", _ => "image/webp" };
            return File(bytes, type);
        }
        catch (ProductImageValidationException) { return NotFound(); }
    }

    private List<AdminProductImageViewModel> DraftRows(string draftId, IReadOnlyList<string> ids)
        => ids.Select((id, index) => new AdminProductImageViewModel
        {
            UploadId = id, DisplayOrder = index,
            ImageUrl = Url.Action(nameof(ProductImageDraft), "Admin", new { draftId, uploadId = id })!
        }).ToList();

    private JsonResult GalleryError(string message, int status = 400) => new(new { error = message }) { StatusCode = status };
}
