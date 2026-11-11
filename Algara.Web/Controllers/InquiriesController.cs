using System.Globalization;
using System.Security.Claims;
using Algara.Data.Data;
using Algara.Data.Models;
using Algara.Identity.Authorization;
using Algara.Web.Authorization;
using Algara.Web.Services.Checkout;
using Algara.Web.Services.Email;
using Algara.Web.Services.Inquiries;
using Algara.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Algara.Web.Controllers;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class InquiriesController(ShopDbContext db, InquiryService service, CheckoutTokens tokens, StaffAccessService access) : Controller
{
    private CancellationToken Ct => HttpContext.RequestAborted;
    private string? Guest(int n) => Request.Cookies["Algara.Inquiry." + n];
    private void SetGuest(int n, string token) => Response.Cookies.Append("Algara.Inquiry." + n, token,
        new CookieOptions { HttpOnly = true, Secure = Request.IsHttps, SameSite = SameSiteMode.Strict,
            Path = "/inquiries", MaxAge = TimeSpan.FromDays(30), IsEssential = true });

    [HttpGet("/inquiries/new")]
    public async Task<IActionResult> New(int? productN, int? variantN = null, int quantity = 1)
    {
        var form = new InquiryForm { ProductN = productN, VariantN = variantN, Quantity = quantity, SubmissionToken = tokens.Issue(InquiryService.Owner(User, false)) };
        if (!await PopulateProduct(form, true)) return NotFound();
        return View("New", form);
    }

    [HttpPost("/inquiries/product"), ValidateAntiForgeryToken]
    public IActionResult ProductEntry(int productN, int? variantN, int quantity = 1) =>
        RedirectToAction(nameof(New), new { productN, variantN, quantity });

    private async Task<bool> PopulateProduct(InquiryForm form, bool initial)
    {
        form.ProductSelection = null;
        if (!form.ProductN.HasValue) return form.VariantN == null;
        var product = await new Algara.Data.Repositories.ProductRepository(db).GetByNAsync(form.ProductN.Value);
        if (product == null || product.IsStaffOnly) return false;
        var language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        form.ProductSelection = InquiryProductSelection.Describe(product, form.VariantN, form.Quantity, language);
        if (form.ProductSelection == null) return false;
        if (initial) {
            form.Dimensions = InquiryProductSelection.Dimensions(product);
            form.Materials = product.GetMaterials(language);
            form.Description = language == "en" ? "Please advise availability and delivery timeframe for the selected item." : "Моля за информация за наличност и срок за доставка на избрания артикул.";
        }
        return true;
    }

    [HttpGet("/admin/inquiries/new"), RequirePermission(PermissionCatalog.Inquiries.View, PermissionCatalog.Inquiries.Create)]
    public IActionResult StaffNew(int? productN) => View("New", new InquiryForm { Staff = true, ProductN = productN, SubmissionToken = tokens.Issue(InquiryService.Owner(User, true)) });

    [HttpPost("/inquiries/new"), ValidateAntiForgeryToken, EnableRateLimiting("Inquiries")]
    [RequestSizeLimit(27 * 1024 * 1024), RequestFormLimits(MultipartBodyLengthLimit = 26 * 1024 * 1024)]
    public Task<IActionResult> Create(InquiryForm form) => CreateCore(form, false);

    // Staff workflow uses permission/CSRF/live session checks, not the public IP-based anti-spam quota.
    [HttpPost("/admin/inquiries/new"), ValidateAntiForgeryToken]
    [RequirePermission(PermissionCatalog.Inquiries.View, PermissionCatalog.Inquiries.Create)]
    [RequestSizeLimit(27 * 1024 * 1024), RequestFormLimits(MultipartBodyLengthLimit = 26 * 1024 * 1024)]
    public Task<IActionResult> StaffCreate(InquiryForm form) => CreateCore(form, true);

    private async Task<IActionResult> CreateCore(InquiryForm form, bool staff)
    {
        form.Staff = staff;
        if (!await PopulateProduct(form, false)) { form.Error = "ProductUnavailable"; return View("New", form); }
        if (!ModelState.IsValid) { form.Error = "Invalid"; return View("New", form); }
        var inquiry = await service.CreateAsync(form, User, CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, Ct);
        if (inquiry == null) return View("New", form);
        if (!staff && inquiry.CustomerId == null)
        {
            var ticket = tokens.Read(form.SubmissionToken, InquiryService.Owner(User, false))!;
            SetGuest(inquiry.N, ticket.GuestSecret);
        }
        return RedirectToAction(staff ? nameof(StaffDetails) : nameof(Details), new { n = inquiry.N });
    }

    [HttpGet("/inquiries"), Authorize]
    public async Task<IActionResult> Mine(int page = 1)
    {
        ViewData["Staff"] = false;
        page = Math.Clamp(page, 1, 100000); ViewData["Page"] = page;
        var id = await service.CurrentCustomerIdAsync(User, Ct);
        if (id == null) return Forbid();
        return View("List", await db.Set<FurnitureInquiry>().AsNoTracking().Where(x => x.CustomerId == id)
            .OrderByDescending(x => x.N).Skip((page - 1) * 50).Take(50).ToListAsync(Ct));
    }

    [HttpGet("/admin/inquiries"), RequirePermission(PermissionCatalog.Inquiries.View)]
    public async Task<IActionResult> Index(string? q, InquiryStatus? status, int page = 1)
    {
        ViewData["Staff"] = true; ViewData["Query"] = q; ViewData["Status"] = status;
        page = Math.Clamp(page, 1, 100000); ViewData["Page"] = page;
        var rows = db.Set<FurnitureInquiry>().AsNoTracking();
        if (status.HasValue) rows = rows.Where(x => x.Status == status);
        if (!string.IsNullOrWhiteSpace(q))
        {
            q = q.Trim(); if (q.Length > 121) q = q[..121];
            int.TryParse(q, out var n);
            rows = rows.Where(x => x.N == n || x.ContactName.Contains(q) || x.Phone.Contains(q));
        }
        return View("List", await rows.OrderByDescending(x => x.N).Skip((page - 1) * 50).Take(50).ToListAsync(Ct));
    }

    [HttpGet("/inquiries/{n:int}")]
    public Task<IActionResult> Details(int n) => DetailCore(n, false);
    [HttpGet("/admin/inquiries/{n:int}"), RequirePermission(PermissionCatalog.Inquiries.View)]
    public Task<IActionResult> StaffDetails(int n) => DetailCore(n, true);

    [HttpGet("/admin/inquiries/products"), RequirePermission(PermissionCatalog.Inquiries.View, PermissionCatalog.Inquiries.Manage,
        PermissionCatalog.Orders.View, PermissionCatalog.Orders.Create, PermissionCatalog.Partners.View)]
    public async Task<IActionResult> Products(string? q) => Json(await ProductChoices(q));

    private async Task<List<AgreedOfferLine>> ProductChoices(string? search)
    {
        var choices = new List<AgreedOfferLine>();
        if (search?.Trim() is not { Length: >= 2 and <= 100 } query) return choices;
        var ids = await db.Products.AsNoTracking().Where(p => p.IsActive &&
            (p.Name.Contains(query) || p.Sku.Contains(query) || p.Variants.Any(v => v.Sku.Contains(query))))
            .OrderBy(p => p.Name).ThenBy(p => p.N).Select(p => p.N).Take(15).ToListAsync(Ct);
        var repository = new Algara.Data.Repositories.ProductRepository(db);
        foreach(var id in ids) {
            var product = await repository.GetByNAsync(id);
            if (product == null) continue;
            var variants = product.OptionKind == ProductOptionKind.None ? new int?[] { null } :
                product.GetSelectableVariants().OrderByDescending(v => v.Sku.Contains(query,StringComparison.OrdinalIgnoreCase)).Select(v => (int?)v.N);
            foreach(var variant in variants.Take(30 - choices.Count))
                if(InquiryOfferBuilder.Selection(product,variant,1,DateTime.UtcNow) is {} choice) choices.Add(choice);
            if(choices.Count == 30) break;
        }
        return choices;
    }

    private async Task<IActionResult> DetailCore(int n, bool staff, string? error = null, InquiryUpdate? draft = null)
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        var inquiry = await db.Set<FurnitureInquiry>().AsNoTracking().SingleOrDefaultAsync(x => x.N == n, Ct);
        if (inquiry == null || !await service.CanReadAsync(inquiry, User, Guest(n), staff, Ct)) return NotFound();
        inquiry.Offers = await db.Set<InquiryOffer>().AsNoTracking().Where(x => x.InquiryN == n).OrderByDescending(x => x.N).ToListAsync(Ct);
        if (staff) inquiry.Events = await db.Set<InquiryEvent>().AsNoTracking().Where(x => x.InquiryN == n).OrderByDescending(x => x.N).ToListAsync(Ct);
        var files = await db.Set<InquiryAttachment>().AsNoTracking().Where(x => x.InquiryN == n).Select(x => new { x.N, x.FileName }).ToListAsync(Ct);
        var canConvert = staff && await access.HasPermissionAsync(User, PermissionCatalog.Inquiries.Manage, Ct)
            && await access.HasPermissionAsync(User, PermissionCatalog.Orders.View, Ct)
            && await access.HasPermissionAsync(User, PermissionCatalog.Orders.Create, Ct)
            && await access.HasPermissionAsync(User, PermissionCatalog.Partners.View, Ct);
        if (draft == null) {
            var latest = inquiry.Offers.FirstOrDefault();
            draft = new InquiryUpdate { Version = Convert.ToBase64String(inquiry.RowVersion), Terms = latest?.Terms,
                Description = latest?.Description ?? inquiry.Description, DeliveryAmount = latest?.DeliveryAmount,
                Lines = AgreedOffer.Lines(latest?.LinesJson), Services = AgreedOffer.Services(latest?.ServicesJson) };
            if (latest == null && inquiry.ProductN.HasValue && inquiry.Quantity.HasValue &&
                await InquiryOfferBuilder.SelectionAsync(db, inquiry.ProductN.Value, inquiry.VariantN, inquiry.Quantity.Value, DateTime.UtcNow) is {} initial) {
                initial.Comment = string.Join("\n", new[] { inquiry.Dimensions, inquiry.Materials, inquiry.Description }.Where(x => !string.IsNullOrWhiteSpace(x)));
                draft.Lines.Add(initial);
            }
        }
        var choices = canConvert ? await ProductChoices(draft.ProductSearch) : [];
        return View("Details", new InquiryDetails { Inquiry = inquiry, Staff = staff, Error = error,
            Draft = draft ?? new InquiryUpdate { Version = Convert.ToBase64String(inquiry.RowVersion) },
            CanConvert = canConvert, Choices = choices.Take(30).ToList(),
            LinkedOrderStatus = inquiry.OrderN.HasValue ? await db.Orders.Where(o => o.N == inquiry.OrderN).Select(o => (OrderStatus?)o.Status).SingleOrDefaultAsync(Ct) : null,
            CanManage = staff && await access.HasPermissionAsync(User, PermissionCatalog.Inquiries.Manage, Ct),
            Attachments = files.Select(x => (x.N, x.FileName)).ToList(),
            AccessLink = !staff && inquiry.CustomerId == null && InquiryService.HasGuestAccess(inquiry, Guest(n)) ?
                Url.Action(nameof(Access)) + "#" + Guest(n) : null });
    }

    [HttpPost("/inquiries/{n:int}/reply"), ValidateAntiForgeryToken, EnableRateLimiting("Inquiries")]
    public Task<IActionResult> Reply(int n, InquiryUpdate form) => UpdateCore(n, form, false);
    [HttpPost("/admin/inquiries/{n:int}"), ValidateAntiForgeryToken]
    [RequirePermission(PermissionCatalog.Inquiries.View, PermissionCatalog.Inquiries.Manage)]
    public async Task<IActionResult> Update(int n, InquiryUpdate form)
    {
        if (form.Lines.Count > 30 || form.Services.Count > 20) return BadRequest();
        if (form.Command is "Search" or "AddService" || form.Command.StartsWith("Add:") || form.Command.StartsWith("Remove:") || form.Command.StartsWith("RemoveService:")) {
            if (!await access.HasPermissionAsync(User, PermissionCatalog.Orders.Create, Ct)) return Forbid();
            if (form.Command.StartsWith("Add:")) {
                var parts = form.Command.Split(':');
                if (parts.Length == 3 && int.TryParse(parts[1], out var product) && int.TryParse(parts[2], out var variant) && form.Lines.Count < 30 &&
                    await InquiryOfferBuilder.SelectionAsync(db, product, variant == 0 ? null : variant, 1, DateTime.UtcNow) is {} line) form.Lines.Add(line);
            }
            if (form.Command.StartsWith("Remove:") && int.TryParse(form.Command[7..], out var index) && index >= 0 && index < form.Lines.Count) { form.Lines.RemoveAt(index); ReindexDraft("Lines",index); }
            if (form.Command.StartsWith("RemoveService:") && int.TryParse(form.Command[14..], out var serviceIndex) && serviceIndex >= 0 && serviceIndex < form.Services.Count) { form.Services.RemoveAt(serviceIndex); ReindexDraft("Services",serviceIndex); }
            if (form.Command == "AddService" && form.Services.Count < 20) form.Services.Add(new("", 0));
            // Structural changes need reindexed controls. Raw invalid decimals stay in ModelState on other actions.
            return await DetailCore(n, true, "DraftOnly", form);
        }
        return await UpdateCore(n, form, true);
    }

    private void ReindexDraft(string prefix, int removed)
    {
        var entries=ModelState.Where(p=>p.Key.StartsWith(prefix+"[",StringComparison.Ordinal)).ToArray();
        foreach(var entry in entries)ModelState.Remove(entry.Key);
        foreach(var entry in entries) {
            var end=entry.Key.IndexOf(']');
            if(end<0 || !int.TryParse(entry.Key[(prefix.Length+1)..end],out var index) || index==removed)continue;
            var key=prefix+"["+(index>removed?index-1:index)+entry.Key[end..];
            ModelState.SetModelValue(key,entry.Value!.RawValue,entry.Value.AttemptedValue);
            foreach(var error in entry.Value.Errors)ModelState.TryAddModelError(key,error.ErrorMessage.Length>0?error.ErrorMessage:"Invalid value.");
            if(entry.Value.Errors.Count==0)ModelState[key]!.ValidationState=Microsoft.AspNetCore.Mvc.ModelBinding.ModelValidationState.Valid;
        }
    }

    // Only decision fields are validated; posted draft fields are retained for redisplay.
    [HttpPost("/admin/inquiries/{n:int}/complete"), ValidateAntiForgeryToken]
    [RequirePermission(PermissionCatalog.Inquiries.View, PermissionCatalog.Inquiries.Manage)]
    public async Task<IActionResult> Complete(int n, InquiryUpdate form)
    {
        if (!await db.Set<InquiryOffer>().AnyAsync(o => o.InquiryN == n, Ct)) return BadRequest();
        // Preserve an unsent offer draft for redisplay only; these fields never enter the decision.
        foreach (var name in ModelState.Keys.Concat(Request.Form.Keys).Distinct().Where(k => k is "Price" or "Terms" or "Description" or "DeliveryAmount" || k.StartsWith("Lines[") || k.StartsWith("Services[")).ToArray())
            if (Request.HasFormContentType && Request.Form.TryGetValue(name, out var value))
            {
                ModelState.Remove(name);
                ModelState.SetModelValue(name, value, value.ToString());
                ModelState[name]!.ValidationState = Microsoft.AspNetCore.Mvc.ModelBinding.ModelValidationState.Skipped;
            }
        form.Command = "Complete";
        return await UpdateCore(n, form, true);
    }

    private async Task<IActionResult> UpdateCore(int n, InquiryUpdate form, bool staff)
    {
        form.N = n;
        if (form.Command is not ("Offer" or "Complete"))
            foreach (var key in ModelState.Keys.Where(k => k.StartsWith("Lines[") || k.StartsWith("Services[") || k == "DeliveryAmount").ToArray()) ModelState.Remove(key);
        if (form.Command is not ("Offer" or "Complete")) {
            ModelState.Remove(nameof(form.Price)); ModelState.Remove(nameof(form.Terms)); ModelState.Remove(nameof(form.Description));
        }
        if (staff) {
            if (string.IsNullOrWhiteSpace(form.Note)) ModelState.AddModelError("Note", "FieldRequired");
            if (form.Command == "Complete" && !form.Agreement) ModelState.AddModelError("Agreement", "Agreement");
            var createsOffer = form.Command == "Offer" || (form.Command == "Complete" && !await db.Set<InquiryOffer>().AnyAsync(o => o.InquiryN == n, Ct));
            if (createsOffer) {
                if (string.IsNullOrWhiteSpace(form.Terms)) ModelState.AddModelError("Terms", "FieldRequired");
                if (string.IsNullOrWhiteSpace(form.Description)) ModelState.AddModelError("Description", "FieldRequired");
                if (form.Lines.Count == 0) ModelState.AddModelError("ProductSearch", "FieldItems");
                for (var i = 0; i < form.Lines.Count; i++) {
                    var line = form.Lines[i]; var key = $"Lines[{i}]";
                    if (line.Quantity is < 1 or > 99) ModelState.AddModelError(key+".Quantity", "FieldQuantity");
                    if (!AgreedOffer.Money(line.UnitPrice)) ModelState.AddModelError(key+".UnitPrice", "FieldPrice");
                    var catalog = await InquiryOfferBuilder.SelectionAsync(db,line.ProductN,line.VariantN,1,DateTime.UtcNow);
                    if (catalog == null) ModelState.AddModelError(key+".Quantity", "FieldInvalid");
                    else if ((catalog.IndividualManufacture || line.IndividualManufacture || catalog.CatalogUnitPrice != line.UnitPrice) && string.IsNullOrWhiteSpace(line.Comment)) ModelState.AddModelError(key+".Comment", "FieldComment");
                    if (line.Comment?.Length > 6000) ModelState.AddModelError(key+".Comment", "FieldInvalid");
                }
                for (var i = 0; i < form.Services.Count; i++) {
                    var item = form.Services[i];
                    if (string.IsNullOrWhiteSpace(item.Name)) ModelState.AddModelError($"Services[{i}].Name", "FieldRequired");
                    else if (item.Name.Length > 100 || item.Name.Any(char.IsControl)) ModelState.AddModelError($"Services[{i}].Name", "FieldInvalid");
                    if (!AgreedOffer.Money(item.Amount,true)) ModelState.AddModelError($"Services[{i}].Amount", "FieldInvalid");
                }
                if (form.DeliveryAmount.HasValue && !AgreedOffer.Money(form.DeliveryAmount.Value,true)) ModelState.AddModelError("DeliveryAmount", "FieldInvalid");
            }
        }
        var error = ModelState.IsValid ? await service.UpdateAsync(form, User, Guest(n), staff, Ct) : "Invalid";
        if (error != null) { Response.StatusCode = error == "AccessDenied" ? 404 : 409; return await DetailCore(n, staff, error, form); }
        return RedirectToAction(staff ? nameof(StaffDetails) : nameof(Details), new { n });
    }

    [HttpGet("/inquiries/{n:int}/files/{id:int}")]
    public Task<IActionResult> Download(int n, int id) => FileCore(n, id, false);
    [HttpGet("/admin/inquiries/{n:int}/files/{id:int}"), RequirePermission(PermissionCatalog.Inquiries.View)]
    public Task<IActionResult> StaffDownload(int n, int id) => FileCore(n, id, true);
    private async Task<IActionResult> FileCore(int n, int id, bool staff)
    {
        var inquiry = await db.Set<FurnitureInquiry>().AsNoTracking().SingleOrDefaultAsync(x => x.N == n, Ct);
        if (inquiry == null || !await service.CanReadAsync(inquiry, User, Guest(n), staff, Ct)) return NotFound();
        var file = await db.Set<InquiryAttachment>().AsNoTracking().SingleOrDefaultAsync(x => x.N == id && x.InquiryN == n, Ct);
        if (file == null) return NotFound();
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Content-Security-Policy"] = "sandbox; default-src 'none'";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        return File(file.Contents, "application/octet-stream", file.FileName);
    }

    [HttpGet("/inquiries/access")]
    public IActionResult Access() { Response.Headers["Referrer-Policy"] = "no-referrer"; return View(); }
    [HttpPost("/inquiries/access"), ValidateAntiForgeryToken, EnableRateLimiting("Inquiries")]
    public async Task<IActionResult> Access(string? token)
    {
        if (!AccountEmailRules.TryParse(token, out var id, out _)) return NotFound();
        var inquiry = await db.Set<FurnitureInquiry>().AsNoTracking().SingleOrDefaultAsync(x => x.SubmissionId == id, Ct);
        if (inquiry == null || !InquiryService.HasGuestAccess(inquiry, token)) return NotFound();
        // The customer account remains authoritative for account-owned inquiries.
        if (inquiry.CustomerId != null && User.Identity?.IsAuthenticated != true)
            return Challenge(new Microsoft.AspNetCore.Authentication.AuthenticationProperties {
                RedirectUri = Url.Action(nameof(Details), new { n = inquiry.N }) });
        if (inquiry.CustomerId == null) SetGuest(inquiry.N, token!);
        return RedirectToAction(nameof(Details), new { n = inquiry.N });
    }
}
