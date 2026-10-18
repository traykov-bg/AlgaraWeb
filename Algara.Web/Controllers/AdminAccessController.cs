using Algara.Identity.Authorization;
using Algara.Identity.Data;
using Algara.Web.Authorization;
using Algara.Web.ViewModels.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Algara.Web.Controllers;

[Authorize(Policy = RequirePermissionAttribute.StaffPolicy)]
[Route("admin/access")]
public sealed class AdminAccessController(IdentityDbContext db, AccessManagementService management) : Controller
{
    [HttpGet("")]
    [RequirePermission(PermissionCatalog.Access.ManagePermissions)]
    public async Task<IActionResult> Index(string roleName = StaffRoles.Moderator)
    {
        if (!StaffRoles.IsStaff(roleName)) return NotFound();
        var role = await db.Roles.AsNoTracking().SingleOrDefaultAsync(r => r.Name == roleName, HttpContext.RequestAborted);
        if (role == null) return NotFound();
        var vm = new AdminAccessFormViewModel
        {
            RoleName = role.Name,
            ExpectedVersion = role.PermissionsVersion,
            PermissionKeys = role.Name == StaffRoles.Admin
                ? PermissionCatalog.All.Select(p => p.Key).ToList()
                : await db.RolePermissions.AsNoTracking().Where(p => p.RoleN == role.N)
                    .Select(p => p.PermissionKey).ToListAsync(HttpContext.RequestAborted)
        };
        await PopulateAsync(vm);
        return View(vm);
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    [RequirePermission(PermissionCatalog.Access.ManagePermissions)]
    public async Task<IActionResult> Index(AdminAccessFormViewModel vm)
    {
        if (!StaffRoles.IsStaff(vm.RoleName)) return BadRequest();
        if (vm.PermissionKeys.Count > PermissionCatalog.All.Count)
            ModelState.AddModelError(string.Empty, "Изпратеният списък с права е невалиден.");
        if (ModelState.IsValid)
        {
            var result = await management.SaveRolePermissionsAsync(User, vm.RoleName, vm.PermissionKeys,
                vm.ExpectedVersion, HttpContext.RequestAborted);
            if (result.Succeeded)
            {
                TempData["Success"] = result.Changed ? "Правата са записани. Промените важат от следващата заявка на служителите." : "Няма промени в правата.";
                return RedirectToAction(nameof(Index), new { roleName = vm.RoleName });
            }
            ModelState.AddModelError(string.Empty, result.Error ?? "Правата не са записани.");
        }
        await PopulateAsync(vm);
        return View(vm);
    }

    [HttpGet("history")]
    [RequirePermission(PermissionCatalog.Access.ViewHistory)]
    public async Task<IActionResult> History(int page = 1)
    {
        page = Math.Clamp(page, 1, 100000);
        var events = await db.AccessAuditEvents.AsNoTracking().OrderByDescending(e => e.Id)
            .Skip((page - 1) * 50).Take(51).ToListAsync(HttpContext.RequestAborted);
        return View(new AdminAccessHistoryViewModel { Events = events.Take(50).ToList(), Page = page, HasNextPage = events.Count > 50 });
    }

    private async Task PopulateAsync(AdminAccessFormViewModel vm)
    {
        vm.AvailableRoles = await db.Roles.AsNoTracking().Where(r => StaffRoles.All.Contains(r.Name))
            .OrderBy(r => r.Name).Select(r => r.Name).ToListAsync(HttpContext.RequestAborted);
    }
}
