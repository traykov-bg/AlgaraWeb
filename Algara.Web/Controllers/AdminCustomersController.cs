using System.Linq.Expressions;
using System.Text.Json;
using System.Security.Claims;
using Algara.Data.Data;
using Algara.Data.Models;
using Algara.Identity.Authorization;
using Algara.Identity.Data;
using Algara.Identity.Models;
using Algara.Web.Authorization;
using Algara.Web.Services;
using Algara.Web.Services.Email;
using Algara.Web.ViewModels.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Algara.Web.Controllers;

[Authorize(Policy = RequirePermissionAttribute.StaffPolicy)]
[Route("admin/users/{n:int}/dossier")]
public sealed class AdminCustomersController(
    IdentityDbContext identityDb, ShopDbContext shopDb,
    StaffAccessService staffAccess, CustomerDossierService dossierService, IAccountEmailService accountEmail,
    ICustomerManagementService customerManagement) : Controller
{
    [HttpGet("")]
    [RequirePermission("Users.View", "Users.ViewDossier")]
    public async Task<IActionResult> Dossier([FromRoute] int n, string? partnerSearch = null,
        int partnerPage = 1, int? selectedPartnerN = null, int historyPage = 1, int orderPage = 1, int addressPage = 1,
        int? editAddressN = null)
    {
        var vm = await LoadAsync(n, partnerSearch, partnerPage, selectedPartnerN, historyPage, orderPage, addressPage, editAddressN);
        return vm == null ? NotFound() : View("~/Views/Admin/CustomerDossier.cshtml", vm);
    }

    [HttpPost("notes"), ValidateAntiForgeryToken]
    [RequirePermission("Users.View", "Users.ViewDossier", "Users.AddNotes")]
    public async Task<IActionResult> AddNote([FromRoute] int n, [Bind(Prefix = "NoteForm")] CustomerNoteForm form)
    {
        if (ModelState.IsValid)
        {
            var result = await dossierService.AddNoteAsync(User, n, form.ExpectedAccountVersion, form.Note, HttpContext.RequestAborted);
            if (result.Success)
            {
                TempData["Success"] = "Служебната бележка е добавена.";
                return RedirectToAction(nameof(Dossier), new { n });
            }
            ModelState.AddModelError(string.Empty, result.Error ?? "Бележката не е записана.");
        }
        var vm = await LoadAsync(n);
        if (vm == null) return NotFound();
        vm.NoteForm = form;
        return View("~/Views/Admin/CustomerDossier.cshtml", vm);
    }

    [HttpPost("link"), ValidateAntiForgeryToken]
    [RequirePermission("Users.View", "Users.ViewDossier", "Users.ManagePartnerLinks", "Partners.View")]
    public async Task<IActionResult> Link([FromRoute] int n, [Bind(Prefix = "LinkForm")] CustomerLinkForm form)
    {
        if (ModelState.IsValid)
        {
            var result = await dossierService.LinkAsync(User, n, form.PartnerN, form.ExpectedAccountVersion,
                form.ExpectedLinkVersion, form.ExpectedPartnerVersion, form.ConfirmedSpecificPartner,
                form.ConfirmedCustomerRequest, form.VerificationMethod, form.Reason, HttpContext.RequestAborted);
            if (result.Success)
            {
                TempData["Success"] = "Акаунтът е свързан с избрания контрагент. Проверка и основание са записани в историята.";
                return RedirectToAction(nameof(Dossier), new { n });
            }
            ModelState.AddModelError(string.Empty, result.Error ?? "Връзката не е записана.");
        }
        var vm = await LoadAsync(n, selectedPartnerN: form.PartnerN);
        if (vm == null) return NotFound();
        vm.LinkForm = form;
        return View("~/Views/Admin/CustomerDossier.cshtml", vm);
    }

    [HttpPost("unlink"), ValidateAntiForgeryToken]
    [RequirePermission("Users.View", "Users.ViewDossier", "Users.ManagePartnerLinks", "Partners.View")]
    public async Task<IActionResult> Unlink([FromRoute] int n, [Bind(Prefix = "UnlinkForm")] CustomerUnlinkForm form)
    {
        if (ModelState.IsValid)
        {
            var result = await dossierService.UnlinkAsync(User, n, form.ExpectedAccountVersion,
                form.ExpectedLinkVersion, form.Reason, HttpContext.RequestAborted);
            if (result.Success)
            {
                TempData["Success"] = "Връзката с контрагента е прекратена. Основанието е записано в историята.";
                return RedirectToAction(nameof(Dossier), new { n });
            }
            ModelState.AddModelError(string.Empty, result.Error ?? "Връзката не е прекратена.");
        }
        var vm = await LoadAsync(n);
        if (vm == null) return NotFound();
        vm.UnlinkForm = form;
        return View("~/Views/Admin/CustomerDossier.cshtml", vm);
    }

    [HttpPost("send-reset"), ValidateAntiForgeryToken]
    [RequirePermission("Users.View", "Users.ViewDossier", "Users.SendAccountEmails")]
    public Task<IActionResult> SendReset([FromRoute] int n, [Bind(Prefix = "EmailForm")] CustomerAccountEmailForm form)
        => SendAccountEmail(n, form, confirmation: false);

    [HttpPost("send-confirmation"), ValidateAntiForgeryToken]
    [RequirePermission("Users.View", "Users.ViewDossier", "Users.SendAccountEmails")]
    public Task<IActionResult> SendConfirmation([FromRoute] int n, [Bind(Prefix = "EmailForm")] CustomerAccountEmailForm form)
        => SendAccountEmail(n, form, confirmation: true);

    private async Task<IActionResult> SendAccountEmail(int n, CustomerAccountEmailForm form, bool confirmation)
    {
        if (ModelState.IsValid)
        {
            var result = confirmation
                ? await accountEmail.SendConfirmationAsync(User, n, form.ExpectedAccountVersion, form.Reason, HttpContext.RequestAborted)
                : await accountEmail.SendResetAsync(User, n, form.ExpectedAccountVersion, form.Reason, HttpContext.RequestAborted);
            if (result.Success)
            {
                TempData["Success"] = "Писмото е добавено за изпращане. Статусът е видим в историята на имейлите.";
                return RedirectToAction(nameof(Dossier), new { n });
            }
            ModelState.AddModelError(string.Empty, result.Error ?? "Писмото не е добавено за изпращане.");
        }
        var vm = await LoadAsync(n);
        if (vm == null) return NotFound();
        vm.EmailForm = form;
        return View("~/Views/Admin/CustomerDossier.cshtml", vm);
    }

    [HttpPost("profile"), ValidateAntiForgeryToken]
    [RequirePermission("Users.View", "Users.ViewDossier", "Users.EditProfile")]
    public Task<IActionResult> EditProfile([FromRoute] int n, [Bind(Prefix = "ProfileForm")] CustomerProfileEditForm form)
        => ManageAsync(n, () => customerManagement.EditProfileAsync(User, n, new CustomerProfileUpdate
        {
            ExpectedAccountVersion = form.ExpectedAccountVersion, Reason = form.Reason,
            FirstName = form.FirstName, LastName = form.LastName, PhoneNumber = form.PhoneNumber
        }, HttpContext.RequestAborted), "Профилните данни са обновени.", vm => vm.ProfileForm = form);

    [HttpPost("addresses"), ValidateAntiForgeryToken]
    [RequirePermission("Users.View", "Users.ViewDossier", "Users.ManageAddresses")]
    public Task<IActionResult> SaveAddress([FromRoute] int n, [Bind(Prefix = "AddressForm")] CustomerAddressEditForm form)
        => ManageAsync(n, () => customerManagement.SaveAddressAsync(User, n, new CustomerAddressUpdate
        {
            N = form.N, ExpectedAccountVersion = form.ExpectedAccountVersion, ExpectedAddressesVersion = form.ExpectedAddressesVersion,
            Reason = form.Reason, FirstName = form.FirstName, LastName = form.LastName, PhoneNumber = form.PhoneNumber,
            Email = form.Email, AddressLine1 = form.AddressLine1, AddressLine2 = form.AddressLine2,
            City = form.City, PostalCode = form.PostalCode, Country = form.Country, IsDefault = form.IsDefault
        }, HttpContext.RequestAborted), form.N.HasValue ? "Адресът е обновен." : "Адресът е добавен.", vm => vm.AddressForm = form);

    [HttpPost("addresses/delete"), ValidateAntiForgeryToken]
    [RequirePermission("Users.View", "Users.ViewDossier", "Users.ManageAddresses")]
    public Task<IActionResult> DeleteAddress([FromRoute] int n, [Bind(Prefix = "AddressActionForm")] CustomerAddressActionForm form)
        => ManageAsync(n, () => customerManagement.DeleteAddressAsync(User, n, AddressAction(form), HttpContext.RequestAborted),
            "Адресът е изтрит. Историята е запазена.", vm => vm.AddressActionForm = form, form.AddressN);

    [HttpPost("addresses/default"), ValidateAntiForgeryToken]
    [RequirePermission("Users.View", "Users.ViewDossier", "Users.ManageAddresses")]
    public Task<IActionResult> SetDefaultAddress([FromRoute] int n, [Bind(Prefix = "AddressActionForm")] CustomerAddressActionForm form)
        => ManageAsync(n, () => customerManagement.SetDefaultAddressAsync(User, n, AddressAction(form), HttpContext.RequestAborted),
            "Основният адрес е сменен.", vm => vm.AddressActionForm = form, form.AddressN);

    [HttpPost("unlock"), ValidateAntiForgeryToken]
    [RequirePermission("Users.View", "Users.ViewDossier", "Users.Unlock")]
    public Task<IActionResult> Unlock([FromRoute] int n, [Bind(Prefix = "UnlockForm")] CustomerManagementActionForm form)
        => ManageAsync(n, () => customerManagement.UnlockAsync(User, n, AccountAction(form), HttpContext.RequestAborted),
            "Блокирането за неуспешни опити е премахнато.", vm => vm.UnlockForm = form);

    [HttpPost("suspend"), ValidateAntiForgeryToken]
    [RequirePermission("Users.View", "Users.ViewDossier", "Users.SuspendAccess")]
    public Task<IActionResult> SuspendAccess([FromRoute] int n, [Bind(Prefix = "AccessForm")] CustomerManagementActionForm form)
        => ManageAsync(n, () => customerManagement.SuspendAccessAsync(User, n, AccountAction(form), HttpContext.RequestAborted),
            "Достъпът е спрян и сесиите са прекратени.", vm => vm.AccessForm = form);

    [HttpPost("restore"), ValidateAntiForgeryToken]
    [RequirePermission("Users.View", "Users.ViewDossier", "Users.SuspendAccess")]
    public Task<IActionResult> RestoreAccess([FromRoute] int n, [Bind(Prefix = "AccessForm")] CustomerManagementActionForm form)
        => ManageAsync(n, () => customerManagement.RestoreAccessAsync(User, n, AccountAction(form), HttpContext.RequestAborted),
            "Достъпът е възстановен. Клиентът трябва да влезе отново.", vm => vm.AccessForm = form);

    [HttpPost("sessions/revoke"), ValidateAntiForgeryToken]
    [RequirePermission("Users.View", "Users.ViewDossier", "Users.ViewSessions", "Users.RevokeSessions")]
    public Task<IActionResult> RevokeSession([FromRoute] int n, [Bind(Prefix = "SessionForm")] CustomerSessionActionForm form)
        => ManageAsync(n, () => customerManagement.RevokeSessionAsync(User, n, new CustomerSessionAction
        {
            ExpectedAccountVersion = form.ExpectedAccountVersion, Reason = form.Reason,
            SessionN = form.SessionN, ExpectedSessionVersion = form.ExpectedSessionVersion
        }, HttpContext.RequestAborted), "Сесията е прекратена.", vm => vm.SessionForm = form);

    [HttpPost("sessions/revoke-all"), ValidateAntiForgeryToken]
    [RequirePermission("Users.View", "Users.ViewDossier", "Users.ViewSessions", "Users.RevokeSessions")]
    public Task<IActionResult> RevokeAllSessions([FromRoute] int n, [Bind(Prefix = "RevokeAllForm")] CustomerManagementActionForm form)
        => ManageAsync(n, () => customerManagement.RevokeAllSessionsAsync(User, n, AccountAction(form), HttpContext.RequestAborted),
            "Всички сесии са прекратени.", vm => vm.RevokeAllForm = form);

    private async Task<IActionResult> ManageAsync(int n, Func<Task<CustomerDossierResult>> command, string message,
        Action<CustomerDossierViewModel> retainForm, int? focusAddressN = null)
    {
        if (ModelState.IsValid)
        {
            var result = await command();
            if (result.Success)
            {
                TempData["Success"] = message;
                return RedirectToAction(nameof(Dossier), new { n });
            }
            ModelState.AddModelError(string.Empty, result.Error ?? "Промяната не е записана. Презаредете досието.");
        }
        var vm = await LoadAsync(n, focusAddressN: focusAddressN);
        if (vm == null) return NotFound();
        retainForm(vm);
        return View("~/Views/Admin/CustomerDossier.cshtml", vm);
    }

    private static CustomerAccountAction AccountAction(CustomerManagementActionForm form) => new()
    { ExpectedAccountVersion = form.ExpectedAccountVersion, Reason = form.Reason };

    private static CustomerAddressAction AddressAction(CustomerAddressActionForm form) => new()
    { AddressN = form.AddressN, ExpectedAccountVersion = form.ExpectedAccountVersion, ExpectedAddressesVersion = form.ExpectedAddressesVersion, Reason = form.Reason };

    private async Task<CustomerDossierViewModel?> LoadAsync(int n, string? partnerSearch = null,
        int partnerPage = 1, int? selectedPartnerN = null, int historyPage = 1, int orderPage = 1, int addressPage = 1,
        int? editAddressN = null, int? focusAddressN = null)
    {
        var ct = HttpContext.RequestAborted;
        // The version source is server-only; credentials and session tokens are never selected.
        var source = await identityDb.Users.AsNoTracking().Where(u => u.N == n).Select(u => new
        {
            Profile = new CustomerProfileViewModel
            {
                N = u.N, UserName = u.UserName, DisplayName = u.DisplayName, FullName = u.FullName,
                FirstName = u.FirstName, LastName = u.LastName, Email = u.Email, PhoneNumber = u.PhoneNumber,
                IsActive = u.IsActive, EmailConfirmed = u.EmailConfirmed, RequireEmailConfirmation = u.RequireEmailConfirmation,
                AccessSuspendedAtUtc = u.AccessSuspendedAtUtc,
                CreatedAt = u.CreatedAt, LastLoginDate = u.LastLoginDate,
                LockoutUntil = u.LockoutUntil, FailedLoginAttempts = u.FailedLoginAttempts,
                AddressLine1 = u.AddressLine1, AddressLine2 = u.AddressLine2, City = u.City,
                PostalCode = u.PostalCode, Country = u.Country
            },
            VersionSource = new ApplicationUser
            {
                N = u.N, Id = u.Id, UserName = u.UserName, Email = u.Email, FullName = u.FullName, DisplayName = u.DisplayName,
                FirstName = u.FirstName, LastName = u.LastName, PhoneNumber = u.PhoneNumber,
                IsActive = u.IsActive, SecurityStamp = u.SecurityStamp, AccessSuspendedAtUtc = u.AccessSuspendedAtUtc,
                FailedLoginAttempts = u.FailedLoginAttempts, LockoutUntil = u.LockoutUntil,
                RequireEmailConfirmation = u.RequireEmailConfirmation, EmailConfirmed = u.EmailConfirmed
            }
        }).SingleOrDefaultAsync(ct);
        if (source == null) return null;

        var vm = new CustomerDossierViewModel
        {
            Customer = source.Profile,
            CanViewOrders = await staffAccess.HasPermissionAsync(User, "Orders.View"),
            CanViewPartners = await staffAccess.HasPermissionAsync(User, "Partners.View"),
            CanViewHistory = await staffAccess.HasPermissionAsync(User, "Users.ViewDossierHistory"),
            CanAddNotes = await staffAccess.HasPermissionAsync(User, "Users.AddNotes"),
            CanManageLinks = await staffAccess.HasPermissionAsync(User, "Users.ManagePartnerLinks"),
            CanSendAccountEmails = await staffAccess.HasPermissionAsync(User, "Users.SendAccountEmails"),
            AddressPage = Math.Clamp(addressPage, 1, 100000),
            HistoryPage = Math.Clamp(historyPage, 1, 100000),
            OrderPage = Math.Clamp(orderPage, 1, 100000),
            PartnerPage = Math.Clamp(partnerPage, 1, 100000),
            PartnerSearch = partnerSearch?.Trim()
        };
        vm.CanManageLinks &= vm.CanViewPartners;
        var accountVersion = CustomerDossierVersions.Account(source.VersionSource);
        vm.NoteForm.ExpectedAccountVersion = accountVersion;
        vm.LinkForm.ExpectedAccountVersion = accountVersion;
        vm.UnlinkForm.ExpectedAccountVersion = accountVersion;
        vm.EmailForm.ExpectedAccountVersion = accountVersion;
        var managementVersion = CustomerManagementVersions.Account(source.VersionSource);
        var actorIsAdmin = await staffAccess.HasPermissionAsync(User, "Access.AssignRoles", ct);
        var staffRoles = StaffRoles.All.ToArray();
        vm.IsProtectedStaffTarget = !actorIsAdmin && await identityDb.UserRoles.AsNoTracking()
            .AnyAsync(r => r.UserN == n && staffRoles.Contains(r.Role.Name), ct);
        var canManageTarget = !vm.IsProtectedStaffTarget && (source.Profile.IsActive || source.Profile.AccessSuspendedAtUtc.HasValue);
        vm.CanEditProfile = canManageTarget && await staffAccess.HasPermissionAsync(User, "Users.EditProfile", ct);
        vm.CanManageAddresses = canManageTarget && await staffAccess.HasPermissionAsync(User, "Users.ManageAddresses", ct);
        vm.CanUnlock = canManageTarget && await staffAccess.HasPermissionAsync(User, "Users.Unlock", ct);
        var canManageAccess = !vm.IsProtectedStaffTarget && await staffAccess.HasPermissionAsync(User, "Users.SuspendAccess", ct);
        vm.CanSuspendAccess = canManageAccess && source.Profile.IsActive && source.VersionSource.Id != User.FindFirstValue(ClaimTypes.NameIdentifier);
        vm.CanRestoreAccess = canManageAccess && !source.Profile.IsActive && source.Profile.AccessSuspendedAtUtc.HasValue;
        vm.CanViewSessions = canManageTarget && await staffAccess.HasPermissionAsync(User, "Users.ViewSessions", ct);
        vm.CanRevokeSessions = vm.CanViewSessions && await staffAccess.HasPermissionAsync(User, "Users.RevokeSessions", ct);
        vm.ProfileForm = new CustomerProfileEditForm
        {
            ExpectedAccountVersion = managementVersion, FirstName = source.Profile.FirstName ?? string.Empty,
            LastName = source.Profile.LastName ?? string.Empty, PhoneNumber = source.Profile.PhoneNumber
        };
        vm.UnlockForm.ExpectedAccountVersion = managementVersion;
        vm.AccessForm.ExpectedAccountVersion = managementVersion;
        vm.RevokeAllForm.ExpectedAccountVersion = managementVersion;
        vm.SessionForm.ExpectedAccountVersion = managementVersion;
        if (vm.CanViewSessions)
        {
            var sessions = await customerManagement.GetSessionsAsync(User, n, ct);
            if (sessions == null) vm.CanViewSessions = vm.CanRevokeSessions = false;
            else
            {
                vm.SessionForm.ExpectedAccountVersion = sessions.AccountVersion;
                vm.RevokeAllForm.ExpectedAccountVersion = sessions.AccountVersion;
                vm.Sessions = sessions.Sessions.Select(s => new CustomerSessionViewModel
                {
                    Id = s.Id, CreatedAt = s.CreatedAt, DeviceInfo = s.DeviceInfo,
                    IsActive = s.IsActive, IsCurrent = s.IsCurrent, Version = s.Version
                }).ToList();
            }
        }
        vm.CanViewEmailHistory = vm.CanSendAccountEmails && vm.CanViewHistory;
        if (vm.CanViewEmailHistory)
        {
            var events = await identityDb.AccountEmailAuditEvents.AsNoTracking().Where(e => e.UserN == n)
                .OrderByDescending(e => e.Id).Take(20)
                .Select(e => new { e.TokenId, e.ActorName, e.Action, e.Reason, e.OccurredAtUtc }).ToListAsync(ct);
            var ids = events.Where(e => e.TokenId.HasValue).Select(e => e.TokenId!.Value).ToArray();
            var statuses = await identityDb.EmailOutboxMessages.AsNoTracking()
                .Where(m => m.TokenId.HasValue && ids.Contains(m.TokenId.Value))
                .OrderByDescending(m => m.CreatedAtUtc).Select(m => new { m.TokenId, m.State }).ToListAsync(ct);
            vm.EmailHistory = events.Select(e => new AccountEmailHistoryViewModel
            {
                ActorName = e.ActorName, Action = e.Action, Reason = e.Reason, OccurredAtUtc = e.OccurredAtUtc,
                DeliveryState = statuses.FirstOrDefault(s => s.TokenId == e.TokenId)?.State
            }).ToList();
        }

        List<UserAddress> addresses;
        if (vm.CanManageAddresses)
        {
            var book = await identityDb.UsersAddresses.AsNoTracking().Where(a => a.UserN == n).OrderBy(a => a.N).ToListAsync(ct);
            var bookVersion = CustomerManagementVersions.AddressBook(book);
            vm.AddressForm = new CustomerAddressEditForm
            {
                ExpectedAccountVersion = managementVersion, ExpectedAddressesVersion = bookVersion,
                FirstName = source.Profile.FirstName ?? string.Empty, LastName = source.Profile.LastName ?? string.Empty,
                PhoneNumber = source.Profile.PhoneNumber, Email = source.Profile.Email, IsDefault = book.Count == 0
            };
            vm.AddressActionForm.ExpectedAccountVersion = managementVersion;
            vm.AddressActionForm.ExpectedAddressesVersion = bookVersion;
            if (editAddressN.HasValue)
            {
                var selected = book.SingleOrDefault(a => a.N == editAddressN);
                if (selected == null) ModelState.AddModelError(string.Empty, "Избраният адрес вече не е наличен в този акаунт. Изберете отново.");
                else vm.AddressForm = new CustomerAddressEditForm
                {
                    N = selected.N, ExpectedAccountVersion = managementVersion, ExpectedAddressesVersion = bookVersion,
                    FirstName = selected.FirstName, LastName = selected.LastName, PhoneNumber = selected.PhoneNumber,
                    Email = selected.Email, AddressLine1 = selected.AddressLine1, AddressLine2 = selected.AddressLine2,
                    City = selected.City, PostalCode = selected.PostalCode, Country = selected.Country, IsDefault = selected.IsDefault
                };
            }
            var orderedBook = book.OrderByDescending(a => a.IsDefault).ThenByDescending(a => a.N).ToList();
            if (focusAddressN.HasValue)
            {
                var index = orderedBook.FindIndex(a => a.N == focusAddressN.Value);
                if (index >= 0) vm.AddressPage = index / 10 + 1;
            }
            addresses = orderedBook.Skip((vm.AddressPage - 1) * 10).Take(11).ToList();
        }
        else addresses = await identityDb.UsersAddresses.AsNoTracking().Where(a => a.UserN == n)
            .OrderByDescending(a => a.IsDefault).ThenByDescending(a => a.N).Skip((vm.AddressPage - 1) * 10).Take(11).ToListAsync(ct);
        vm.Addresses = addresses.Take(10).Select(a => new CustomerAddressViewModel
        {
            N = a.N, FirstName = a.FirstName, LastName = a.LastName, PhoneNumber = a.PhoneNumber, Email = a.Email,
            AddressLine1 = a.AddressLine1, AddressLine2 = a.AddressLine2, City = a.City,
            PostalCode = a.PostalCode, Country = a.Country, IsDefault = a.IsDefault
        }).ToList();
        vm.HasMoreAddresses = addresses.Count > 10;

        if (vm.CanViewOrders)
        {
            var orders = await shopDb.Orders.AsNoTracking().Where(o => o.UserN == n)
                .OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.N)
                .Skip((vm.OrderPage - 1) * 10).Take(11).Select(o => new CustomerOrderViewModel
                { N = o.N, Status = o.Status, TotalAmount = o.TotalAmount, CreatedAt = o.CreatedAt }).ToListAsync(ct);
            vm.Orders = orders.Take(10).ToList();
            vm.HasMoreOrders = orders.Count > 10;
        }

        if (vm.CanViewPartners)
        {
            var link = await shopDb.PartnerAccountLinks.AsNoTracking().SingleOrDefaultAsync(l => l.UserN == n, ct);
            var linkVersion = CustomerDossierVersions.Link(link);
            vm.LinkForm.ExpectedLinkVersion = linkVersion;
            vm.UnlinkForm.ExpectedLinkVersion = linkVersion;
            if (link?.PartnerN != null)
                vm.Partner = await shopDb.Partners.AsNoTracking().Where(p => p.N == link.PartnerN).Select(PartnerProjection).SingleOrDefaultAsync(ct);

            if (vm.CanManageLinks && vm.Customer.IsActive && link?.PartnerN == null)
            {
                var query = shopDb.Partners.AsNoTracking().Where(p => p.IsActive);
                if (!string.IsNullOrWhiteSpace(vm.PartnerSearch))
                {
                    var search = vm.PartnerSearch[..Math.Min(vm.PartnerSearch.Length, 200)];
                    vm.PartnerSearch = search;
                    var key = PartnerCatalogRules.Key(search)!;
                    var identifier = PartnerCatalogRules.Identifier(search)!;
                    query = query.Where(p => p.Code.Contains(search) || p.NormalizedSiteName.Contains(key) ||
                        (p.Company != null && (p.Company.Name.Contains(search) ||
                            (p.Company.NormalizedRegistrationNumber != null && p.Company.NormalizedRegistrationNumber.Contains(identifier)))));
                }
                var choices = await query.OrderBy(p => EF.Functions.Collate(p.SiteName, "Cyrillic_General_CI_AS")).ThenBy(p => p.N)
                    .Skip((vm.PartnerPage - 1) * 10).Take(11).Select(PartnerProjection).ToListAsync(ct);
                vm.PartnerChoices = choices.Take(10).ToList();
                vm.HasMorePartners = choices.Count > 10;
                if (selectedPartnerN.HasValue)
                {
                    vm.SelectedPartner = await shopDb.Partners.AsNoTracking()
                        .Where(p => p.N == selectedPartnerN && p.IsActive).Select(PartnerProjection).SingleOrDefaultAsync(ct);
                    if (vm.SelectedPartner != null)
                    {
                        vm.LinkForm.PartnerN = vm.SelectedPartner.N;
                        vm.LinkForm.ExpectedPartnerVersion = vm.SelectedPartner.RowVersion;
                    }
                    else ModelState.AddModelError(string.Empty, "Избраният контрагент вече не е наличен или е неактивен. Изберете отново.");
                }
            }
        }

        if (vm.CanViewHistory)
        {
            // History families keep their own visibility gates; never expose raw audit snapshots.
            var query = shopDb.CustomerDossierEvents.AsNoTracking().Where(e => e.UserN == n);
            if (!vm.CanViewPartners) query = query.Where(e => e.Action != "PartnerLinked" && e.Action != "PartnerUnlinked");
            if (!vm.CanViewSessions) query = query.Where(e => e.Action != "SessionRevoked" && e.Action != "AllSessionsRevoked");
            var events = await query.OrderByDescending(e => e.N).Skip((vm.HistoryPage - 1) * 20).Take(21)
                .Select(e => new { e.N, e.Action, e.ActorName, e.OccurredAtUtc, e.Reason, e.VerificationMethod, e.Note, e.BeforeJson, e.AfterJson })
                .ToListAsync(ct);
            vm.HasMoreHistory = events.Count > 20;
            vm.History = events.Take(20).Select(e => new CustomerHistoryViewModel
            {
                N = e.N, Action = e.Action, ActorName = e.ActorName, OccurredAtUtc = e.OccurredAtUtc,
                Reason = e.Reason, VerificationMethod = e.VerificationMethod, Note = e.Note,
                BeforePartner = e.Action is "PartnerLinked" or "PartnerUnlinked" ? PartnerSnapshotLabel(e.BeforeJson) : null,
                AfterPartner = e.Action is "PartnerLinked" or "PartnerUnlinked" ? PartnerSnapshotLabel(e.AfterJson) : null,
                Changes = CustomerManagementHistory.Format(e.Action, e.BeforeJson, e.AfterJson)
            }).ToList();
        }
        return vm;
    }

    private static readonly Expression<Func<Partner, CustomerPartnerViewModel>> PartnerProjection = p => new CustomerPartnerViewModel
    {
        N = p.N, Code = p.Code, Name = p.SiteName, CompanyName = p.Company == null ? null : p.Company.Name,
        RegistrationNumber = p.Company == null ? null : p.Company.RegistrationNumber,
        Phone = p.Phone, Email = p.Email, City = p.Address.City, Street = p.Address.Street,
        IsActive = p.IsActive, AllowAccountLinks = p.AllowAccountLinks, RowVersion = Convert.ToBase64String(p.RowVersion)
    };

    private static string? PartnerSnapshotLabel(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("PartnerN", out var number) ||
                number.ValueKind == JsonValueKind.Null) return null;
            var code = root.TryGetProperty("Code", out var codeValue) && codeValue.ValueKind == JsonValueKind.String ? codeValue.GetString() : null;
            var name = root.TryGetProperty("Name", out var nameValue) && nameValue.ValueKind == JsonValueKind.String ? nameValue.GetString() : null;
            return string.Join(" · ", new[] { code, name }.Where(v => !string.IsNullOrWhiteSpace(v)));
        }
        catch (JsonException) { return "Исторически запис"; }
    }
}
