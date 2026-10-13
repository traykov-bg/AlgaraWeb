using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Algara.Data.Models;
using Algara.Web.ViewModels.Admin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Algara.Web.Controllers;

public partial class AdminController
{
    [HttpGet("companies")]
    public async Task<IActionResult> Companies(string? search = null, bool? active = null, int page = 1)
    {
        search = PartnerCatalogRules.Text(search);
        var key = PartnerCatalogRules.Identifier(search);
        var query = _shopDb.Companies.AsNoTracking();
        if (active.HasValue) query = query.Where(c => c.IsActive == active);
        if (search != null) query = query.Where(c => c.Name.Contains(search) ||
            (c.LegalForm != null && c.LegalForm.Contains(search)) ||
            (c.NormalizedRegistrationNumber != null && c.NormalizedRegistrationNumber.Contains(key!)) ||
            (c.VatNumber != null && c.VatNumber.Contains(key!)) ||
            (c.Email != null && c.Email.Contains(search)) || (c.Address.City != null && c.Address.City.Contains(search)));
        var total = await query.CountAsync();
        var pages = Math.Max(1, (int)Math.Ceiling(total / 20d));
        page = Math.Clamp(page, 1, pages);
        return View(new CompanyListModel
        {
            Search = search, Active = active, Page = page, TotalPages = pages, Total = total,
            Items = await query.OrderByDescending(c => key != null && c.NormalizedRegistrationNumber == key)
                .ThenBy(c => c.Name).ThenBy(c => c.N).Skip((page - 1) * 20).Take(20).ToListAsync()
        });
    }

    [HttpGet("companies/create")]
    public IActionResult CompanyCreate() => View("CompanyEdit", new CompanyForm());

    [HttpGet("companies/{n:int}/edit")]
    public async Task<IActionResult> CompanyEdit([FromRoute] int n)
    {
        var c = await _shopDb.Companies.AsNoTracking().SingleOrDefaultAsync(c => c.N == n);
        if (c == null) return NotFound();
        var form = new CompanyForm
        {
            N = n, RowVersion = Convert.ToBase64String(c.RowVersion), Name = c.Name, LegalForm = c.LegalForm,
            CountryCode = c.CountryCode, RegistrationType = c.RegistrationType, RegistrationNumber = c.RegistrationNumber,
            IsVatRegistered = c.IsVatRegistered, VatNumber = c.VatNumber, Representative = c.Representative,
            Phone = c.Phone, Email = c.Email, AccountingContact = c.AccountingContact, Address = c.Address,
            CorrespondenceAddress = c.CorrespondenceAddress, Notes = c.Notes, IsActive = c.IsActive
        };
        await CatalogHistory(form, "Company");
        return View(form);
    }

    [HttpPost("companies/create"), ValidateAntiForgeryToken]
    public Task<IActionResult> CompanyCreate(CompanyForm form)
    {
        form.N = null;
        return SaveCompany(form);
    }

    [HttpPost("companies/{n:int}/edit"), ValidateAntiForgeryToken]
    public Task<IActionResult> CompanyEdit([FromRoute] int n, CompanyForm form)
        => form.N != n ? Task.FromResult<IActionResult>(BadRequest()) : SaveCompany(form);

    private Task<IActionResult> SaveCompany(CompanyForm form) => SaveCatalog<Company, CompanyForm>(form,
        "Company", "CompanyEdit", "Companies", "Name", async existing =>
        {
            var number = PartnerCatalogRules.Identifier(form.RegistrationNumber);
            var country = form.CountryCode.Trim().ToUpperInvariant();
            if (number != null && await _shopDb.Companies.AnyAsync(c => c.N != (form.N ?? 0) &&
                c.CountryCode == country && c.RegistrationType == form.RegistrationType && c.NormalizedRegistrationNumber == number))
                ModelState.AddModelError(nameof(form.RegistrationNumber), "Вече има фирма с този регистрационен номер, вид и държава. Проверете и неактивните фирми.");
            return () =>
            {
                var c = existing ?? new Company();
                c.Name = PartnerCatalogRules.Text(form.Name)!;
                c.LegalForm = PartnerCatalogRules.Text(form.LegalForm);
                c.CountryCode = country;
                c.RegistrationType = form.RegistrationType;
                c.RegistrationNumber = PartnerCatalogRules.Text(form.RegistrationNumber);
                c.NormalizedRegistrationNumber = number;
                c.IsVatRegistered = form.IsVatRegistered;
                c.VatNumber = PartnerCatalogRules.Identifier(form.VatNumber);
                c.Representative = PartnerCatalogRules.Text(form.Representative);
                PartnerCatalogRules.TryPhone(form.Phone, out var phone);
                c.Phone = phone;
                c.Email = PartnerCatalogRules.Text(form.Email);
                c.AccountingContact = PartnerCatalogRules.Text(form.AccountingContact);
                CopyAddress(form.Address, c.Address);
                CopyAddress(form.CorrespondenceAddress, c.CorrespondenceAddress);
                c.Notes = form.Notes?.Trim();
                c.IsActive = form.IsActive;
                return c;
            };
        }, _ => Task.CompletedTask);

    [HttpGet("partners")]
    public async Task<IActionResult> Partners(string? search = null, bool? active = null,
        int? companyN = null, int groupN = 0, bool all = false, bool includeChildren = false, int page = 1)
    {
        search = PartnerCatalogRules.Text(search);
        var key = PartnerCatalogRules.Key(search);
        var identifier = PartnerCatalogRules.Identifier(search);
        PartnerCatalogRules.TryPartnerPhone(search, out var phone);
        var groups = await _shopDb.PartnerGroups.AsNoTracking().ToListAsync();
        if (!all && !groups.Any(g => g.N == groupN)) return NotFound();
        var query = _shopDb.Partners.AsNoTracking();
        if (active.HasValue) query = query.Where(p => p.IsActive == active);
        if (companyN.HasValue) query = query.Where(p => p.CompanyN == companyN);
        if (!all)
        {
            var ids = includeChildren ? PartnerCatalogRules.Descendants(groups, groupN) : [groupN];
            query = query.Where(p => ids.Contains(p.GroupN));
        }
        if (search != null) query = query.Where(p => p.Code.Contains(search) ||
            p.NormalizedSiteName.Contains(key!) ||
            (p.Phone != null && (p.Phone.Contains(search) || (phone != null && p.Phone == phone))) ||
            (p.Email != null && p.Email.Contains(search)) || (p.ContactPerson != null && p.ContactPerson.Contains(search)) ||
            (p.Address.City != null && p.Address.City.Contains(search)) || (p.Address.Street != null && p.Address.Street.Contains(search)) ||
            (p.Company != null && (p.Company.Name.Contains(search) ||
                (p.Company.NormalizedRegistrationNumber != null && p.Company.NormalizedRegistrationNumber.Contains(identifier!)) ||
                (p.Company.VatNumber != null && p.Company.VatNumber.Contains(identifier!)))));
        var total = await query.CountAsync();
        var pages = Math.Max(1, (int)Math.Ceiling(total / 20d));
        page = Math.Clamp(page, 1, pages);
        var counts = await _shopDb.Partners.GroupBy(p => p.GroupN).Select(g => new { N = g.Key, Count = g.Count() }).ToListAsync();
        return View(new PartnerListModel
        {
            MoveSelectionKey = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty))),
            Search = search, All = all, Active = active, CompanyN = companyN, GroupN = groupN, IncludeChildren = includeChildren,
            Page = page, TotalPages = pages, Total = total,
            Items = await query.Include(p => p.Company).Include(p => p.Group)
                .OrderByDescending(p => search != null && p.Code == search)
                .ThenByDescending(p => identifier != null && p.Company != null && p.Company.NormalizedRegistrationNumber == identifier)
                .ThenBy(p => EF.Functions.Collate(p.SiteName, "Cyrillic_General_CI_AS")).ThenBy(p => p.N)
                .Skip((page - 1) * 20).Take(20).ToListAsync(),
            Groups = GroupRows(groups, counts.ToDictionary(c => c.N, c => c.Count)),
            Companies = await CompanyChoices()
        });
    }

    [HttpGet("partners/create")]
    public async Task<IActionResult> PartnerCreate(int groupN = 0)
    {
        if (!await _shopDb.PartnerGroups.AnyAsync(g => g.N == groupN)) return NotFound();
        var form = new PartnerForm { GroupN = groupN };
        await PartnerChoices(form);
        return View("PartnerEdit", form);
    }

    [HttpGet("partners/{n:int}/edit")]
    public async Task<IActionResult> PartnerEdit([FromRoute] int n)
    {
        var p = await _shopDb.Partners.AsNoTracking().SingleOrDefaultAsync(p => p.N == n);
        if (p == null) return NotFound();
        var form = new PartnerForm
        {
            N = n, RowVersion = Convert.ToBase64String(p.RowVersion), Code = p.Code, SiteName = p.SiteName,
            CompanyN = p.CompanyN, GroupN = p.GroupN, Phone = p.Phone, Email = p.Email, ContactPerson = p.ContactPerson,
            Address = p.Address, DeliveryAddress = p.DeliveryAddress, Notes = p.Notes, IsActive = p.IsActive
        };
        await PartnerChoices(form);
        await CatalogHistory(form, "Partner");
        return View(form);
    }

    [HttpPost("partners/create"), ValidateAntiForgeryToken]
    public Task<IActionResult> PartnerCreate(PartnerForm form)
    {
        form.N = null;
        return SavePartner(form);
    }

    [HttpPost("partners/{n:int}/edit"), ValidateAntiForgeryToken]
    public Task<IActionResult> PartnerEdit([FromRoute] int n, PartnerForm form)
        => form.N != n ? Task.FromResult<IActionResult>(BadRequest()) : SavePartner(form);

    private Task<IActionResult> SavePartner(PartnerForm form) => SaveCatalog<Partner, PartnerForm>(form,
        "Partner", "PartnerEdit", "Partners", "Code", async existing =>
        {
            if (!await _shopDb.PartnerGroups.AnyAsync(g => g.N == form.GroupN))
                ModelState.AddModelError(nameof(form.GroupN), "Групата не съществува.");
            var existingCompanyN = existing?.CompanyN;
            if (form.CompanyN.HasValue && !await _shopDb.Companies.AnyAsync(c => c.N == form.CompanyN && (c.IsActive || c.N == existingCompanyN)))
                ModelState.AddModelError(nameof(form.CompanyN), "Изберете съществуваща активна фирма.");
            var site = PartnerCatalogRules.Key(form.SiteName);
            PartnerCatalogRules.TryPartnerPhone(form.Phone, out var phone);
            if (await _shopDb.Partners.AnyAsync(p => p.N != (form.N ?? 0) && p.NormalizedSiteName == site))
                ModelState.AddModelError(nameof(form.SiteName), "Вече има контрагент с това име. Проверете и неактивните контрагенти.");
            var code = form.Code?.Trim();
            if (string.IsNullOrEmpty(code) && existing != null) code = existing.Code;
            if (!string.IsNullOrEmpty(code) && await _shopDb.Partners.AnyAsync(p => p.N != (form.N ?? 0) && p.Code == code))
                ModelState.AddModelError(nameof(form.Code), "Вече има контрагент с този код.");
            if (ModelState.IsValid && string.IsNullOrEmpty(code))
            {
                do
                {
                    var numbers = await _shopDb.Database.SqlQueryRaw<long>("SELECT NEXT VALUE FOR [PartnerCodeNumbers] AS [Value]").ToListAsync();
                    code = numbers.Single().ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
                } while (await _shopDb.Partners.AnyAsync(p => p.Code == code));
            }
            return () =>
            {
                var p = existing ?? new Partner();
                p.Code = code!;
                p.Kind = form.CompanyN.HasValue ? PartnerKind.CompanySite : PartnerKind.Person;
                p.SiteName = PartnerCatalogRules.Text(form.SiteName)!;
                p.NormalizedSiteName = site!;
                p.CompanyN = form.CompanyN;
                p.GroupN = form.GroupN;
                p.Phone = phone;
                p.Email = PartnerCatalogRules.Text(form.Email);
                p.ContactPerson = PartnerCatalogRules.Text(form.ContactPerson);
                CopyAddress(form.Address, p.Address);
                CopyAddress(form.DeliveryAddress, p.DeliveryAddress);
                p.Notes = form.Notes?.Trim();
                p.IsActive = form.IsActive;
                return p;
            };
        }, PartnerChoices);

    [HttpGet("partner-groups")]
    public IActionResult PartnerGroups() => RedirectToAction(nameof(Partners));

    [HttpGet("partner-groups/create")]
    public async Task<IActionResult> PartnerGroupCreate(int parentN = 0)
    {
        var form = new PartnerGroupForm { ParentN = parentN };
        await GroupChoices(form);
        return View("PartnerGroupEdit", form);
    }

    [HttpGet("partner-groups/{n:int}/edit")]
    public async Task<IActionResult> PartnerGroupEdit([FromRoute] int n)
    {
        var g = await _shopDb.PartnerGroups.AsNoTracking().SingleOrDefaultAsync(g => g.N == n);
        if (g == null) return NotFound();
        var form = new PartnerGroupForm { N = n, Name = g.Name, ParentN = g.ParentN, RowVersion = Convert.ToBase64String(g.RowVersion) };
        await GroupChoices(form);
        await CatalogHistory(form, "Group");
        return View(form);
    }

    [HttpPost("partner-groups/create"), ValidateAntiForgeryToken]
    public Task<IActionResult> PartnerGroupCreate(PartnerGroupForm form)
    {
        form.N = null;
        return SavePartnerGroup(form);
    }

    [HttpPost("partner-groups/{n:int}/edit"), ValidateAntiForgeryToken]
    public Task<IActionResult> PartnerGroupEdit([FromRoute] int n, PartnerGroupForm form)
        => form.N != n ? Task.FromResult<IActionResult>(BadRequest()) : SavePartnerGroup(form);

    private Task<IActionResult> SavePartnerGroup(PartnerGroupForm form) => SaveCatalog<PartnerGroup, PartnerGroupForm>(form,
        "Group", "PartnerGroupEdit", "Partners", "Name", async existing =>
        {
            var groups = await _shopDb.PartnerGroups.AsNoTracking().ToListAsync();
            if (form.N == 0 && form.ParentN.HasValue)
                ModelState.AddModelError(nameof(form.ParentN), "Основната група не може да се премества.");
            if (form.N != 0 && !form.ParentN.HasValue)
                ModelState.AddModelError(nameof(form.ParentN), "Изберете родителска група.");
            if (form.ParentN.HasValue && !groups.Any(g => g.N == form.ParentN))
                ModelState.AddModelError(nameof(form.ParentN), "Родителската група не съществува.");
            if (PartnerCatalogRules.WouldCreateCycle(groups, form.N ?? -1, form.ParentN))
                ModelState.AddModelError(nameof(form.ParentN), "Група не може да се премества в себе си или в своя подгрупа.");
            var key = PartnerCatalogRules.Key(form.Name)!;
            if (groups.Any(g => g.N != (form.N ?? -1) && g.ParentN == form.ParentN && g.NormalizedName == key))
                ModelState.AddModelError(nameof(form.Name), "В това ниво вече има група с това име.");
            return () =>
            {
                var g = existing ?? new PartnerGroup();
                g.Name = PartnerCatalogRules.Text(form.Name)!;
                g.NormalizedName = key;
                g.ParentN = form.ParentN;
                return g;
            };
        }, GroupChoices);

    private async Task<IActionResult> SaveCatalog<TEntity, TForm>(TForm form, string entityType, string editView,
        string listAction, string touchProperty, Func<TEntity?, Task<Func<TEntity>>> prepare, Func<TForm, Task> choices)
        where TEntity : class, new() where TForm : CatalogEditForm
    {
        var actor = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(actor)) return Forbid();
        if (ModelState.IsValid)
        {
            await using var transaction = await _shopDb.Database.BeginTransactionAsync();
            try
            {
                await LockPartnerCatalog();
                var existing = form.N.HasValue ? await _shopDb.Set<TEntity>().FindAsync(form.N.Value) : null;
                if (form.N.HasValue && existing == null) return NotFound();
                if (existing != null)
                {
                    var current = (byte[])_shopDb.Entry(existing).Property("RowVersion").CurrentValue!;
                    var posted = new byte[8];
                    if (form.RowVersion == null || !Convert.TryFromBase64String(form.RowVersion, posted, out var length) ||
                        length != 8 || !current.SequenceEqual(posted))
                        ModelState.AddModelError(string.Empty, "Записът е променен от друг служител. Отворете го отново и нанесете промените върху актуалните данни.");
                }
                if (ModelState.IsValid)
                {
                    var apply = await prepare(existing);
                    if (ModelState.IsValid)
                    {
                        var before = existing == null ? null : CatalogSnapshot(existing);
                        var entity = apply();
                        if (existing == null) _shopDb.Add(entity);
                        else _shopDb.Entry(entity).Property(touchProperty).IsModified = true;
                        await _shopDb.SaveChangesAsync();
                        var id = (int)_shopDb.Entry(entity).Property("N").CurrentValue!;
                        _shopDb.PartnerCatalogChanges.Add(new PartnerCatalogChange
                        {
                            EntityType = entityType, EntityN = id, ActorId = actor, ActorName = User.Identity?.Name ?? actor,
                            CreatedAtUtc = DateTime.UtcNow, BeforeJson = before, AfterJson = CatalogSnapshot(entity)
                        });
                        await _shopDb.SaveChangesAsync();
                        await transaction.CommitAsync();
                        TempData["Success"] = "Промените са записани.";
                        return entity switch
                        {
                            Partner partner => RedirectToAction(listAction, new { groupN = partner.GroupN }),
                            PartnerGroup group => RedirectToAction(listAction, new { groupN = group.N }),
                            _ => RedirectToAction(listAction)
                        };
                    }
                }
            }
            catch (DbUpdateConcurrencyException)
            {
                ModelState.AddModelError(string.Empty, "Записът е променен. Отворете го отново преди редакция.");
            }
            catch (DbUpdateException ex) when (ex.InnerException is SqlException sql && sql.Number is 2601 or 2627)
            {
                ModelState.AddModelError(string.Empty, "Вече съществува запис със същия код или идентифициращи данни. Проверете и неактивните записи.");
            }
            catch (SqlException ex) when (ex.Number is 51002 or 1205)
            {
                ModelState.AddModelError(string.Empty, "Картотеката е заета. Опитайте да запишете отново.");
            }
            await transaction.RollbackAsync();
            _shopDb.ChangeTracker.Clear();
        }
        await choices(form);
        await CatalogHistory(form, entityType);
        return View(editView, form);
    }

    // All catalog writers share this lock, including bulk moves and code allocation.
    private Task<int> LockPartnerCatalog() => _shopDb.Database.ExecuteSqlRawAsync("""
        DECLARE @result int;
        EXEC @result = sys.sp_getapplock @Resource=N'Algara.PartnerCatalog', @LockMode='Exclusive',
            @LockOwner='Transaction', @LockTimeout=15000;
        IF @result < 0 THROW 51002, 'Partner catalog is busy.', 1;
        """);

    private static readonly JsonSerializerOptions CatalogJson = new() { ReferenceHandler = ReferenceHandler.IgnoreCycles };
    private static string CatalogSnapshot<T>(T entity) => JsonSerializer.Serialize(entity, CatalogJson);

    private async Task CatalogHistory(CatalogEditForm form, string type)
    {
        if (form.N.HasValue) form.History = await _shopDb.PartnerCatalogChanges.AsNoTracking()
            .Where(c => c.EntityType == type && c.EntityN == form.N).OrderByDescending(c => c.N).Take(10).ToListAsync();
    }

    private async Task<List<SelectListItem>> CompanyChoices() => await _shopDb.Companies.AsNoTracking()
        .OrderBy(c => c.Name).ThenBy(c => c.N).Select(c => new SelectListItem
        {
            Value = c.N.ToString(), Text = c.Name + " " + c.LegalForm + " · " + (c.RegistrationNumber ?? "без рег. номер") + (c.IsActive ? "" : " (неактивна)")
        }).ToListAsync();

    private async Task PartnerChoices(PartnerForm form)
    {
        form.Companies = await CompanyChoices();
        form.Groups = GroupRows(await _shopDb.PartnerGroups.AsNoTracking().ToListAsync())
            .Select(g => new SelectListItem(g.Path, g.N.ToString())).ToList();
    }

    private async Task GroupChoices(PartnerGroupForm form)
    {
        var groups = await _shopDb.PartnerGroups.AsNoTracking().ToListAsync();
        var excluded = form.N.HasValue ? PartnerCatalogRules.Descendants(groups, form.N.Value) : [];
        form.Parents = GroupRows(groups).Where(g => !excluded.Contains(g.N))
            .Select(g => new SelectListItem(g.Path, g.N.ToString())).ToList();
    }

    private static List<PartnerGroupRow> GroupRows(List<PartnerGroup> groups, Dictionary<int, int>? counts = null)
    {
        var rows = new List<PartnerGroupRow>();
        var children = groups.OrderBy(g => g.Name, StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("bg-BG"), true))
            .ThenBy(g => g.N).ToLookup(g => g.ParentN);
        var pending = new Stack<(PartnerGroup Group, string Prefix, int Depth)>(children[null].Reverse().Select(g => (g, "", 0)));
        var visited = new HashSet<int>();
        while (pending.TryPop(out var item))
        {
            if (!visited.Add(item.Group.N)) continue;
            var path = item.Prefix + item.Group.Name;
            rows.Add(new(item.Group.N, item.Group.Name, path, item.Depth, counts?.GetValueOrDefault(item.Group.N) ?? 0,
                item.Group.ParentN, Convert.ToBase64String(item.Group.RowVersion)));
            foreach (var child in children[item.Group.N].Reverse()) pending.Push((child, path + " / ", item.Depth + 1));
        }
        return rows;
    }

    private static void CopyAddress(CatalogAddress source, CatalogAddress target)
    {
        target.Country = PartnerCatalogRules.Text(source.Country);
        target.PostalCode = PartnerCatalogRules.Text(source.PostalCode);
        target.City = PartnerCatalogRules.Text(source.City);
        target.Street = PartnerCatalogRules.Text(source.Street);
        target.Building = PartnerCatalogRules.Text(source.Building);
        target.Entrance = PartnerCatalogRules.Text(source.Entrance);
        target.Floor = PartnerCatalogRules.Text(source.Floor);
        target.Apartment = PartnerCatalogRules.Text(source.Apartment);
        target.Details = PartnerCatalogRules.Text(source.Details);
    }
}
