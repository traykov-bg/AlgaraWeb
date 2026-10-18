using Algara.Web.Authorization;
using System.Security.Claims;
using Algara.Data.Models;
using Algara.Web.ViewModels.Admin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Algara.Web.Controllers;

public partial class AdminController
{
    [HttpPost("partners/move"), ValidateAntiForgeryToken, RequestSizeLimit(262144)]
    [RequirePermission("Partners.View")]
    public async Task<IActionResult> MovePartners([FromBody] PartnerMoveRequest? request)
    {
        if (!ModelState.IsValid || request?.DestinationGroupN is not >= 0 || request.Groups == null || request.Partners == null)
            return BadRequest(new { message = "Невалидни данни за преместване. Обновете страницата и маркирайте записите отново." });
        if (request.Partners.Count > 0 && !await CanAsync("Partners.Move") ||
            request.Groups.Count > 0 && (!await CanAsync("PartnerGroups.View") || !await CanAsync("PartnerGroups.Move")))
            return Forbid();
        var count = request.Groups.Count + request.Partners.Count;
        if (count == 0 || count > PartnerMoveRequest.SelectionLimit)
            return BadRequest(new { message = $"Маркирайте между 1 и {PartnerMoveRequest.SelectionLimit} групи и контрагенти общо." });
        if (!TryMoveVersions(request.Groups, out var groupVersions) || !TryMoveVersions(request.Partners, out var partnerVersions) ||
            !TryMoveVersion(request.DestinationRowVersion, out var destinationVersion))
            return BadRequest(new { message = "Маркираните записи са невалидни или се повтарят. Обновете страницата и ги маркирайте отново." });

        var actor = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(actor)) return Forbid();
        await using var transaction = await _shopDb.Database.BeginTransactionAsync();
        try
        {
            await LockPartnerCatalog();
            var groups = await _shopDb.PartnerGroups.AsNoTracking().ToListAsync();
            var byId = groups.ToDictionary(g => g.N);
            var destinationN = request.DestinationGroupN.Value;
            if (!byId.TryGetValue(destinationN, out var destination) || !destination.RowVersion.SequenceEqual(destinationVersion))
                return MoveConflict("Целевата група е променена или вече не съществува. Обновете страницата и изберете целта отново.");
            foreach (var (n, version) in groupVersions)
                if (!byId.TryGetValue(n, out var group) || !group.RowVersion.SequenceEqual(version))
                    return MoveConflict("Някоя от маркираните групи е променена или вече не съществува. Обновете страницата преди преместване.");

            var partnerIds = partnerVersions.Keys.ToArray();
            var partners = await _shopDb.Partners.AsNoTracking().Where(p => partnerIds.Contains(p.N)).ToListAsync();
            if (partners.Count != partnerIds.Length || partners.Any(p => !p.RowVersion.SequenceEqual(partnerVersions[p.N])))
                return MoveConflict("Някой от маркираните контрагенти е променен или вече не съществува. Обновете страницата преди преместване.");

            // Validate the whole batch before attaching or changing any entity.
            var plan = PartnerMovePlan.Create(groups, partners, groupVersions.Keys.ToArray(), destinationN);
            var beforeGroups = plan.GroupsToMove.ToDictionary(g => g.N, CatalogSnapshot);
            var beforePartners = plan.PartnersToMove.ToDictionary(p => p.N, CatalogSnapshot);
            foreach (var group in plan.GroupsToMove)
            {
                _shopDb.Attach(group);
                group.ParentN = destinationN;
            }
            foreach (var partner in plan.PartnersToMove)
            {
                _shopDb.Attach(partner);
                partner.GroupN = destinationN;
            }
            await _shopDb.SaveChangesAsync();
            var now = DateTime.UtcNow;
            var actorName = User.Identity?.Name ?? actor;
            foreach (var group in plan.GroupsToMove)
                _shopDb.PartnerCatalogChanges.Add(new PartnerCatalogChange
                {
                    EntityType = "Group", EntityN = group.N, ActorId = actor, ActorName = actorName,
                    CreatedAtUtc = now, BeforeJson = beforeGroups[group.N], AfterJson = CatalogSnapshot(group)
                });
            foreach (var partner in plan.PartnersToMove)
                _shopDb.PartnerCatalogChanges.Add(new PartnerCatalogChange
                {
                    EntityType = "Partner", EntityN = partner.N, ActorId = actor, ActorName = actorName,
                    CreatedAtUtc = now, BeforeJson = beforePartners[partner.N], AfterJson = CatalogSnapshot(partner)
                });
            await _shopDb.SaveChangesAsync();
            await transaction.CommitAsync();
            var message = $"Преместени групи: {plan.GroupsToMove.Count}; контрагенти: {plan.PartnersToMove.Count}. Подгрупите и съдържанието на преместените групи са запазени.";
            TempData["Success"] = message;
            return Ok(new { message, redirectUrl = Url.Action(nameof(Partners), new { groupN = destinationN }) });
        }
        catch (PartnerMoveException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (DbUpdateConcurrencyException)
        {
            return MoveConflict("Записите са променени междувременно. Нищо не е преместено. Обновете страницата и опитайте отново.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException sql && sql.Number is 2601 or 2627 or 547)
        {
            return MoveConflict("Преместването е отказано заради променени групи или еднакви имена в целевата група. Нищо не е преместено. Обновете страницата.");
        }
        catch (SqlException ex) when (ex.Number is 51002 or 1205)
        {
            return MoveConflict("Картотеката е заета. Нищо не е преместено. Опитайте отново.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException sql && sql.Number is 51002 or 1205)
        {
            return MoveConflict("Картотеката е заета. Нищо не е преместено. Опитайте отново.");
        }
    }

    private ConflictObjectResult MoveConflict(string message) => Conflict(new { message });

    private static bool TryMoveVersion(string? text, out byte[] version)
    {
        version = new byte[8];
        return text != null && Convert.TryFromBase64String(text, version, out var length) && length == 8;
    }

    private static bool TryMoveVersions(IEnumerable<PartnerMoveSelection> selected, out Dictionary<int, byte[]> versions)
    {
        versions = [];
        foreach (var item in selected)
            if (item?.N is not >= 0 || !TryMoveVersion(item.RowVersion, out var version) || !versions.TryAdd(item.N.Value, version))
                return false;
        return true;
    }
}
