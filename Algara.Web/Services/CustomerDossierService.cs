using System.Security.Claims;
using System.Text.Json;
using Algara.Data.Data;
using Algara.Data.Models;
using Algara.Identity.Authorization;
using Algara.Identity.Data;
using Algara.Identity.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Algara.Web.Services;

public sealed record CustomerDossierResult(bool Success, string? Error = null)
{
    public static CustomerDossierResult Reject(string error) => new(false, error);
}

/// <summary>
/// Account validation, partner validation, association and audit share the same SQL connection
/// and transaction. The lock order also serializes with role changes, self-deletion and catalog edits.
/// </summary>
public sealed class CustomerDossierService(ShopDbContext shop)
{
    public async Task<CustomerDossierResult> AddNoteAsync(ClaimsPrincipal actor, int userN,
        string? expectedAccountVersion, string? note, CancellationToken cancellationToken = default)
    {
        note = note?.Trim();
        if (string.IsNullOrEmpty(note) || note.Length > 4000)
            return CustomerDossierResult.Reject("Въведете бележка до 4000 знака.");
        return await ExecuteAsync(actor, userN, expectedAccountVersion, PermissionCatalog.Users.AddNotes,
            async (identity, target, staff) =>
            {
                AddEvent(staff, target.N, "NoteAdded", "Вътрешна бележка", note: note);
                await shop.SaveChangesAsync(cancellationToken);
                return new(true);
            }, cancellationToken);
    }

    public async Task<CustomerDossierResult> LinkAsync(ClaimsPrincipal actor, int userN, int partnerN,
        string? expectedAccountVersion, string? expectedLinkVersion, string? expectedPartnerVersion,
        bool confirmedSpecificPartner, bool confirmedCustomerRequest, string? verificationMethod,
        string? reason, CancellationToken cancellationToken = default)
    {
        reason = reason?.Trim();
        if (partnerN <= 0 || !CustomerDossierVersions.IsLinkVersion(expectedLinkVersion) ||
            !CustomerDossierVersions.IsRowVersion(expectedPartnerVersion))
            return CustomerDossierResult.Reject("Липсва или е невалидна версията на връзката/контрагента. Презаредете досието.");
        if (!confirmedSpecificPartner || !confirmedCustomerRequest)
            return CustomerDossierResult.Reject("Потвърдете искането и проверената принадлежност към конкретен клиент/обект. Общ запис за различни купувачи не може да се свързва.");
        if (verificationMethod is not ("in_person" or "known_contact" or "documented_request"))
            return CustomerDossierResult.Reject("Изберете как е проверена принадлежността към обекта.");
        if (string.IsNullOrEmpty(reason) || reason.Length > 2000)
            return CustomerDossierResult.Reject("Опишете искането и извършената проверка с до 2000 знака.");

        return await ExecuteAsync(actor, userN, expectedAccountVersion, PermissionCatalog.Users.ManagePartnerLinks,
            async (identity, target, staff) =>
            {
                if (!target.IsActive)
                    return CustomerDossierResult.Reject("Неактивен акаунт не може да се свързва.");
                var link = await shop.PartnerAccountLinks.SingleOrDefaultAsync(l => l.UserN == userN, cancellationToken);
                if (link != null) await shop.Entry(link).ReloadAsync(cancellationToken);
                if (CustomerDossierVersions.Link(link) != expectedLinkVersion)
                    return CustomerDossierResult.Reject("Връзката е променена. Презаредете досието.");
                if (link?.PartnerN != null)
                    return CustomerDossierResult.Reject("Акаунтът вече е свързан. Първо прекратете текущата връзка с основание.");
                var partner = await shop.Partners.SingleOrDefaultAsync(p => p.N == partnerN, cancellationToken);
                if (partner != null) await shop.Entry(partner).ReloadAsync(cancellationToken);
                if (partner == null || !partner.IsActive)
                    return CustomerDossierResult.Reject("Избраният контрагент не съществува или не е активен.");
                if (Convert.ToBase64String(partner.RowVersion) != expectedPartnerVersion)
                    return CustomerDossierResult.Reject("Контрагентът е променен. Презаредете избора и проверете данните.");

                var before = Snapshot(partner, linked: false);
                partner.AllowAccountLinks = true;
                if (link == null)
                {
                    link = new PartnerAccountLink { UserN = userN };
                    shop.PartnerAccountLinks.Add(link);
                }
                link.PartnerN = partner.N;
                AddEvent(staff, userN, "PartnerLinked", reason, verificationMethod,
                    before, Snapshot(partner, linked: true));
                await shop.SaveChangesAsync(cancellationToken);
                return new(true);
            }, cancellationToken);
    }

    public async Task<CustomerDossierResult> UnlinkAsync(ClaimsPrincipal actor, int userN,
        string? expectedAccountVersion, string? expectedLinkVersion, string? reason,
        CancellationToken cancellationToken = default)
    {
        reason = reason?.Trim();
        if (!CustomerDossierVersions.IsRowVersion(expectedLinkVersion))
            return CustomerDossierResult.Reject("Липсва или е невалидна версията на връзката. Презаредете досието.");
        if (string.IsNullOrEmpty(reason) || reason.Length > 2000)
            return CustomerDossierResult.Reject("Въведете основание за прекратяване до 2000 знака.");
        return await ExecuteAsync(actor, userN, expectedAccountVersion, PermissionCatalog.Users.ManagePartnerLinks,
            async (identity, target, staff) =>
            {
                var link = await shop.PartnerAccountLinks.SingleOrDefaultAsync(l => l.UserN == userN, cancellationToken);
                if (link != null) await shop.Entry(link).ReloadAsync(cancellationToken);
                if (link?.PartnerN == null || CustomerDossierVersions.Link(link) != expectedLinkVersion)
                    return CustomerDossierResult.Reject("Връзката вече е променена или прекратена. Презаредете досието.");
                var partner = await shop.Partners.SingleAsync(p => p.N == link.PartnerN, cancellationToken);
                await shop.Entry(partner).ReloadAsync(cancellationToken);
                var before = Snapshot(partner, linked: true);
                var after = Snapshot(partner, linked: false);
                link.PartnerN = null;
                link.Partner = null;
                AddEvent(staff, target.N, "PartnerUnlinked", reason, before: before, after: after);
                await shop.SaveChangesAsync(cancellationToken);
                return new(true);
            }, cancellationToken);
    }

    private async Task<CustomerDossierResult> ExecuteAsync(ClaimsPrincipal actor, int userN,
        string? expectedAccountVersion, string permission,
        Func<IdentityDbContext, ApplicationUser, ApplicationUser, Task<CustomerDossierResult>> action,
        CancellationToken cancellationToken)
    {
        if (userN <= 0 || !CustomerDossierVersions.IsAccountVersion(expectedAccountVersion))
            return CustomerDossierResult.Reject("Липсва или е невалидна версията на акаунта. Презаредете досието.");
        try
        {
            await using var transaction = await AcquireAsync(cancellationToken);
            await using var identity = new IdentityDbContext(new DbContextOptionsBuilder<IdentityDbContext>()
                .UseSqlServer(shop.Database.GetDbConnection()).Options);
            await identity.Database.UseTransactionAsync(transaction.GetDbTransaction(), cancellationToken);
            // A new access service avoids trusting the authorization filter's earlier request snapshot.
            var access = new StaffAccessService(identity);
            var required = new List<string> { PermissionCatalog.Users.View, PermissionCatalog.Users.ViewDossier, permission };
            if (permission == PermissionCatalog.Users.ManagePartnerLinks) required.Add(PermissionCatalog.Partners.View);
            foreach (var key in required)
                if (!await access.HasPermissionAsync(actor, key, cancellationToken))
                    return CustomerDossierResult.Reject("Нямате актуално право за това действие.");
            var actorId = actor.FindFirstValue(ClaimTypes.NameIdentifier);
            var staff = await identity.Users.AsNoTracking().SingleAsync(u => u.Id == actorId, cancellationToken);
            // Also serialize with self-service profile changes, which do not use the access application lock.
            var target = await identity.Users.FromSqlInterpolated(
                $"SELECT * FROM [Users] WITH (UPDLOCK, HOLDLOCK) WHERE [N] = {userN}")
                .AsNoTracking().SingleOrDefaultAsync(cancellationToken);
            if (target == null || CustomerDossierVersions.Account(target) != expectedAccountVersion)
                return CustomerDossierResult.Reject("Акаунтът е променен или не съществува. Презаредете досието.");
            var result = await action(identity, target, staff);
            if (!result.Success) return result;
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (DbUpdateException)
        {
            shop.ChangeTracker.Clear();
            return CustomerDossierResult.Reject("Записът не е извършен. Данните са променени или историята не може да се запише. Презаредете и опитайте отново.");
        }
        catch (SqlException ex) when (ex.Number is 1205 or 1222 or 51004)
        {
            shop.ChangeTracker.Clear();
            return CustomerDossierResult.Reject("Данните се обработват от друго действие. Презаредете и опитайте отново.");
        }
    }

    private async Task<IDbContextTransaction> AcquireAsync(CancellationToken cancellationToken)
    {
        var transaction = await shop.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await shop.Database.ExecuteSqlRawAsync("""
                DECLARE @result int;
                EXEC @result = sys.sp_getapplock @Resource=N'Algara.AccessManagement', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=15000;
                IF @result < 0 THROW 51004, 'Access management is busy.', 1;
                EXEC @result = sys.sp_getapplock @Resource=N'Algara.PartnerCatalog', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=15000;
                IF @result < 0 THROW 51004, 'Partner catalog is busy.', 1;
                """, cancellationToken);
            return transaction;
        }
        catch { await transaction.DisposeAsync(); throw; }
    }

    private static object Snapshot(Partner partner, bool linked) => new
    {
        PartnerN = linked ? (int?)partner.N : null,
        Code = partner.Code, Name = partner.SiteName, partner.AllowAccountLinks
    };

    private void AddEvent(ApplicationUser staff, int userN, string action, string reason,
        string? verificationMethod = null, object? before = null, object? after = null, string? note = null)
    {
        var actorName = string.IsNullOrWhiteSpace(staff.DisplayName) ? staff.UserName : staff.DisplayName;
        shop.CustomerDossierEvents.Add(new CustomerDossierEvent
        {
            UserN = userN, Action = action, ActorId = staff.Id,
            ActorName = actorName.Length <= 256 ? actorName : actorName[..256],
            OccurredAtUtc = DateTime.UtcNow, Reason = reason, VerificationMethod = verificationMethod,
            BeforeJson = before == null ? null : JsonSerializer.Serialize(before),
            AfterJson = after == null ? null : JsonSerializer.Serialize(after), Note = note
        });
    }
}
