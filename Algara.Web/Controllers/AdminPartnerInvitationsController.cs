using Algara.Data.Data;
using Algara.Identity.Data;
using Algara.Identity.Authorization;
using Algara.Identity.Models;
using Algara.Web.Authorization;
using Algara.Web.Services.Email;
using Algara.Web.ViewModels.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Algara.Web.Controllers;

[Authorize(Policy = RequirePermissionAttribute.StaffPolicy)]
[RequirePermission("Users.View", "Users.ViewDossier", "Users.ManagePartnerLinks", "Users.ManageInvitations", "Partners.View")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("admin/partners/{n:int}/invitations")]
public sealed class AdminPartnerInvitationsController(IdentityDbContext identityDb, ShopDbContext shopDb, IAccountEmailService accountEmail, StaffAccessService staffAccess) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index([FromRoute] int n, int page = 1)
    {
        var vm = await LoadAsync(n, page);
        return vm == null ? NotFound() : View(vm);
    }

    [HttpPost(""), ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([FromRoute] int n, [Bind(Prefix = "Form")] PartnerInvitationForm form)
    {
        if (ModelState.IsValid)
        {
            var result = await accountEmail.InviteAsync(User, new PartnerInvitationRequest
            {
                PartnerN = n, ExpectedPartnerVersion = form.ExpectedPartnerVersion, Email = form.Email,
                ConfirmedSpecificPartner = form.ConfirmedSpecificPartner, ConfirmedCustomerRequest = form.ConfirmedCustomerRequest,
                VerificationMethod = form.VerificationMethod, Reason = form.Reason
            }, HttpContext.RequestAborted);
            if (result.Success)
            {
                TempData["Success"] = "Поканата е създадена и добавена за изпращане.";
                return RedirectToAction(nameof(Index), new { n });
            }
            ModelState.AddModelError(string.Empty, result.Error ?? "Поканата не е създадена.");
        }
        var vm = await LoadAsync(n);
        if (vm == null) return NotFound();
        vm.Form = form;
        return View("Index", vm);
    }

    [HttpPost("{id:guid}/resend"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Resend([FromRoute] int n, [FromRoute] Guid id, PartnerInvitationVersionForm form)
    {
        if (!await BelongsToPartner(n, id)) return NotFound();
        if (ModelState.IsValid)
        {
            var result = await accountEmail.ResendInvitationAsync(User, id, form.ExpectedVersion, HttpContext.RequestAborted);
            if (result.Success)
            {
                TempData["Success"] = "Създадена е нова покана. Предишната връзка вече е невалидна.";
                return RedirectToAction(nameof(Index), new { n });
            }
            ModelState.AddModelError(string.Empty, result.Error ?? "Поканата не е подновена.");
        }
        return await InvalidAction(n);
    }

    [HttpPost("{id:guid}/cancel"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel([FromRoute] int n, [FromRoute] Guid id, PartnerInvitationCancelForm form)
    {
        if (!await BelongsToPartner(n, id)) return NotFound();
        if (ModelState.IsValid)
        {
            var result = await accountEmail.CancelInvitationAsync(User, id, form.ExpectedVersion, form.Reason, HttpContext.RequestAborted);
            if (result.Success)
            {
                TempData["Success"] = "Поканата е отменена.";
                return RedirectToAction(nameof(Index), new { n });
            }
            ModelState.AddModelError(string.Empty, result.Error ?? "Поканата не е отменена.");
        }
        return await InvalidAction(n);
    }

    private Task<bool> BelongsToPartner(int n, Guid id) => identityDb.AccountEmailTokens.AsNoTracking()
        .AnyAsync(t => t.Id == id && t.PartnerN == n && t.Purpose == AccountEmailPurpose.PartnerInvitation, HttpContext.RequestAborted);

    private async Task<IActionResult> InvalidAction(int n)
    {
        // A failed command must be reviewed again against the newly displayed row version.
        ModelState.Remove("ExpectedVersion");
        var vm = await LoadAsync(n);
        return vm == null ? NotFound() : View("Index", vm);
    }

    private async Task<PartnerInvitationPage?> LoadAsync(int n, int page = 1)
    {
        var ct = HttpContext.RequestAborted;
        var partner = await shopDb.Partners.AsNoTracking().Where(p => p.N == n)
            .Select(p => new { p.N, p.Code, p.SiteName, p.IsActive, p.RowVersion }).SingleOrDefaultAsync(ct);
        if (partner == null) return null;
        page = Math.Clamp(page, 1, 100000);
        var rows = await identityDb.AccountEmailTokens.AsNoTracking()
            .Where(t => t.PartnerN == n && t.Purpose == AccountEmailPurpose.PartnerInvitation)
            .OrderByDescending(t => t.CreatedAtUtc).ThenByDescending(t => t.Id).Skip((page - 1) * 20).Take(21)
            .Select(t => new PartnerInvitationRow
            {
                Id = t.Id, Email = t.Email, Version = Convert.ToBase64String(t.RowVersion), CreatedByName = t.CreatedByName,
                CreatedAtUtc = t.CreatedAtUtc, ExpiresAtUtc = t.ExpiresAtUtc, ConsumedAtUtc = t.ConsumedAtUtc,
                RevokedAtUtc = t.RevokedAtUtc, Reason = t.Reason, VerificationMethod = t.VerificationMethod
            }).ToListAsync(ct);
        var ids = rows.Take(20).Select(t => t.Id).ToArray();
        var statuses = await identityDb.EmailOutboxMessages.AsNoTracking()
            .Where(m => m.TokenId.HasValue && ids.Contains(m.TokenId.Value))
            .OrderByDescending(m => m.CreatedAtUtc).Select(m => new { m.TokenId, m.State }).ToListAsync(ct);
        foreach (var row in rows.Take(20)) row.DeliveryState = statuses.FirstOrDefault(m => m.TokenId == row.Id)?.State;
        var vm = new PartnerInvitationPage
        {
            PartnerN = n, PartnerName = partner.SiteName, PartnerCode = partner.Code, PartnerActive = partner.IsActive,
            Form = new PartnerInvitationForm { ExpectedPartnerVersion = Convert.ToBase64String(partner.RowVersion) },
            Page = page, HasMore = rows.Count > 20, Items = rows.Take(20).ToList()
        };
        vm.CanViewHistory = await staffAccess.HasPermissionAsync(User, "Users.ViewDossierHistory", ct);
        if (vm.CanViewHistory)
            vm.History = await identityDb.AccountEmailAuditEvents.AsNoTracking().Where(e => e.PartnerN == n)
                .OrderByDescending(e => e.Id).Take(20).Select(e => new AccountEmailHistoryViewModel
                {
                    ActorName = e.ActorName, Action = e.Action, OccurredAtUtc = e.OccurredAtUtc, Reason = e.Reason
                }).ToListAsync(ct);
        return vm;
    }
}
