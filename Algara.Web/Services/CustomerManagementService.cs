using System.Security.Claims;
using System.Text.Json;
using Algara.Data.Data;
using Algara.Data.Models;
using Algara.Identity.Authorization;
using Algara.Identity.Data;
using Algara.Identity.Models;
using Algara.Identity.Services;
using Algara.Web.Services.Email;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Algara.Web.Services;

/// <summary>Profile, address and access changes share current authorization, optimistic versions and atomic history.</summary>
public sealed class CustomerManagementService(ShopDbContext shop) : ICustomerManagementService
{
    private const string Stale = "Данните са променени или не съществуват. Презаредете страницата.";
    private const string Denied = "Нямате актуално право за това действие или акаунтът не може да бъде управляван.";
    private const string OwnReason = "Промяна от собственика на акаунта.";

    public Task<CustomerDossierResult> EditProfileAsync(ClaimsPrincipal actor, int userN, CustomerProfileUpdate request, CancellationToken cancellationToken = default) =>
        EditProfileAsync(actor, (int?)userN, request, cancellationToken);

    public Task<CustomerDossierResult> EditOwnProfileAsync(ClaimsPrincipal actor, CustomerProfileUpdate request, CancellationToken cancellationToken = default) =>
        EditProfileAsync(actor, null, request, cancellationToken);

    private Task<CustomerDossierResult> EditProfileAsync(ClaimsPrincipal actor, int? userN, CustomerProfileUpdate request, CancellationToken cancellationToken)
    {
        var error = CustomerManagementRules.Validate(request);
        if (error != null && request.AssistanceSessionId == null) return RejectAsync(error);
        return ExecuteAsync(actor, userN, request, PermissionCatalog.Users.EditProfile, (identity, target, authenticated) =>
        {
            if (error != null) return Task.FromResult(Change.Reject(error));
            var before = Profile(target);
            var fullName = $"{request.FirstName} {request.LastName}";
            if (target.FirstName == request.FirstName && target.LastName == request.LastName && target.PhoneNumber == request.PhoneNumber &&
                target.FullName == fullName && target.DisplayName == fullName) return Task.FromResult(Change.None);
            target.FirstName = request.FirstName;
            target.LastName = request.LastName;
            target.PhoneNumber = request.PhoneNumber;
            target.FullName = target.DisplayName = fullName;
            return Task.FromResult(Change.Apply("ProfileUpdated", before, () => Profile(target)));
        }, cancellationToken, "ProfileUpdated");
    }

    public Task<CustomerDossierResult> SaveAddressAsync(ClaimsPrincipal actor, int userN, CustomerAddressUpdate request, CancellationToken cancellationToken = default) =>
        SaveAddressAsync(actor, (int?)userN, request, cancellationToken);

    public Task<CustomerDossierResult> SaveOwnAddressAsync(ClaimsPrincipal actor, CustomerAddressUpdate request, CancellationToken cancellationToken = default) =>
        SaveAddressAsync(actor, null, request, cancellationToken);

    private Task<CustomerDossierResult> SaveAddressAsync(ClaimsPrincipal actor, int? userN, CustomerAddressUpdate request, CancellationToken cancellationToken)
    {
        var error = CustomerManagementRules.Validate(request);
        if (!CustomerManagementVersions.IsVersion(request.ExpectedAddressesVersion)) error ??= Stale;
        if (error != null && request.AssistanceSessionId == null) return RejectAsync(error);
        return ExecuteAsync(actor, userN, request, PermissionCatalog.Users.ManageAddresses, async (identity, target, authenticated) =>
        {
            if (error != null) return Change.Reject(error);
            var addresses = await LoadAddressesAsync(identity, target.N, cancellationToken);
            if (CustomerManagementVersions.AddressBook(addresses) != request.ExpectedAddressesVersion) return Change.Reject(Stale);
            var address = request.N.HasValue ? addresses.SingleOrDefault(a => a.N == request.N.Value) : null;
            if (request.N.HasValue && address == null) return Change.Reject(Stale);
            var before = Addresses(addresses);
            var now = DateTime.UtcNow;
            if (address == null)
            {
                address = new UserAddress { UserN = target.N, CreatedAt = now };
                identity.UsersAddresses.Add(address);
                addresses.Add(address);
            }
            address.FirstName = request.FirstName; address.LastName = request.LastName;
            address.PhoneNumber = request.PhoneNumber; address.Email = request.Email;
            address.AddressLine1 = request.AddressLine1; address.AddressLine2 = request.AddressLine2;
            address.City = request.City; address.PostalCode = request.PostalCode; address.Country = request.Country;
            // A checkbox can select a default; clearing it cannot leave an address book without one.
            EnsureDefault(addresses, request.IsDefault ? address : null, now);
            if (JsonSerializer.Serialize(before) == JsonSerializer.Serialize(Addresses(addresses))) return Change.None;
            address.UpdatedAt = request.N.HasValue ? now : null;
            return Change.Apply(request.N.HasValue ? "AddressUpdated" : "AddressCreated", before, () => Addresses(addresses));
        }, cancellationToken, request.N.HasValue ? "AddressUpdated" : "AddressCreated");
    }

    public Task<CustomerDossierResult> DeleteAddressAsync(ClaimsPrincipal actor, int userN, CustomerAddressAction request, CancellationToken cancellationToken = default) =>
        AddressActionAsync(actor, userN, request, false, cancellationToken);

    public Task<CustomerDossierResult> DeleteOwnAddressAsync(ClaimsPrincipal actor, CustomerAddressAction request, CancellationToken cancellationToken = default) =>
        AddressActionAsync(actor, null, request, false, cancellationToken);

    public Task<CustomerDossierResult> SetDefaultAddressAsync(ClaimsPrincipal actor, int userN, CustomerAddressAction request, CancellationToken cancellationToken = default) =>
        AddressActionAsync(actor, userN, request, true, cancellationToken);

    public Task<CustomerDossierResult> SetDefaultOwnAddressAsync(ClaimsPrincipal actor, CustomerAddressAction request, CancellationToken cancellationToken = default) =>
        AddressActionAsync(actor, null, request, true, cancellationToken);

    private Task<CustomerDossierResult> AddressActionAsync(ClaimsPrincipal actor, int? userN, CustomerAddressAction request,
        bool setDefault, CancellationToken cancellationToken)
    {
        var invalid = request.AddressN <= 0 || !CustomerManagementVersions.IsVersion(request.ExpectedAddressesVersion);
        if (invalid && request.AssistanceSessionId == null) return RejectAsync(Stale);
        return ExecuteAsync(actor, userN, request, PermissionCatalog.Users.ManageAddresses, async (identity, target, authenticated) =>
        {
            if (invalid) return Change.Reject(Stale);
            var addresses = await LoadAddressesAsync(identity, target.N, cancellationToken);
            if (CustomerManagementVersions.AddressBook(addresses) != request.ExpectedAddressesVersion) return Change.Reject(Stale);
            var address = addresses.SingleOrDefault(a => a.N == request.AddressN);
            if (address == null) return Change.Reject(Stale);
            var before = Addresses(addresses);
            if (setDefault)
            {
                EnsureDefault(addresses, address, DateTime.UtcNow);
                if (JsonSerializer.Serialize(before) == JsonSerializer.Serialize(Addresses(addresses))) return Change.None;
            }
            else
            {
                identity.UsersAddresses.Remove(address);
                addresses.Remove(address);
                EnsureDefault(addresses, null, DateTime.UtcNow);
            }
            return Change.Apply(setDefault ? "DefaultAddressChanged" : "AddressDeleted", before, () => Addresses(addresses));
        }, cancellationToken, setDefault ? "DefaultAddressChanged" : "AddressDeleted");
    }

    public Task<CustomerDossierResult> UnlockAsync(ClaimsPrincipal actor, int userN, CustomerAccountAction request, CancellationToken cancellationToken = default) =>
        ExecuteAsync(actor, userN, request, PermissionCatalog.Users.Unlock, (identity, target, authenticated) =>
        {
            if (target.FailedLoginAttempts == 0 && target.LockoutUntil == null) return Task.FromResult(Change.None);
            var before = Access(target);
            target.FailedLoginAttempts = 0;
            target.LockoutUntil = null;
            return Task.FromResult(Change.Apply("AccountUnlocked", before, () => Access(target)));
        }, cancellationToken);

    public Task<CustomerDossierResult> SuspendAccessAsync(ClaimsPrincipal actor, int userN, CustomerAccountAction request, CancellationToken cancellationToken = default) =>
        ExecuteAsync(actor, userN, request, PermissionCatalog.Users.SuspendAccess, async (identity, target, authenticated) =>
        {
            if (target.N == authenticated.N) return Change.Reject("Не можете да спрете собствения си достъп.");
            if (!target.IsActive) return Change.Reject("Достъпът вече е спрян. Презаредете досието.");
            if (await IsLastAdministratorAsync(identity, target.N, cancellationToken))
                return Change.Reject("Последният активен администратор трябва да запази достъпа си.");
            var before = Access(target);
            target.IsActive = false;
            target.AccessSuspendedAtUtc = DateTime.UtcNow;
            await InvalidateSessionsAsync(identity, target, cancellationToken);
            return Change.Apply("AccessSuspended", before, () => Access(target));
        }, cancellationToken);

    public Task<CustomerDossierResult> RestoreAccessAsync(ClaimsPrincipal actor, int userN, CustomerAccountAction request, CancellationToken cancellationToken = default) =>
        ExecuteAsync(actor, userN, request, PermissionCatalog.Users.SuspendAccess, async (identity, target, authenticated) =>
        {
            if (target.IsActive || !target.AccessSuspendedAtUtc.HasValue)
                return Change.Reject("Може да се възстанови само достъп, временно спрян през досието.");
            var before = Access(target);
            target.IsActive = true;
            target.AccessSuspendedAtUtc = null;
            await InvalidateSessionsAsync(identity, target, cancellationToken);
            return Change.Apply("AccessRestored", before, () => Access(target));
        }, cancellationToken);

    public Task<CustomerDossierResult> RevokeSessionAsync(ClaimsPrincipal actor, int userN, CustomerSessionAction request, CancellationToken cancellationToken = default)
    {
        if (request.SessionN <= 0 || !CustomerManagementVersions.IsVersion(request.ExpectedSessionVersion)) return RejectAsync(Stale);
        return ExecuteAsync(actor, userN, request, PermissionCatalog.Users.RevokeSessions, async (identity, target, authenticated) =>
        {
            var session = await identity.UserSessions.FromSqlInterpolated(
                $"SELECT * FROM [UserSessions] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]={request.SessionN} AND [UserN]={target.N}")
                .SingleOrDefaultAsync(cancellationToken);
            if (session == null || !session.IsActive || CustomerManagementVersions.Session(session) != request.ExpectedSessionVersion) return Change.Reject(Stale);
            session.IsActive = false;
            return Change.Apply("SessionRevoked", new { SessionN = session.Id, IsActive = true },
                () => new { SessionN = session.Id, IsActive = false });
        }, cancellationToken);
    }

    public Task<CustomerDossierResult> RevokeAllSessionsAsync(ClaimsPrincipal actor, int userN, CustomerAccountAction request, CancellationToken cancellationToken = default) =>
        ExecuteAsync(actor, userN, request, PermissionCatalog.Users.RevokeSessions, async (identity, target, authenticated) =>
        {
            var count = await InvalidateSessionsAsync(identity, target, cancellationToken);
            return Change.Apply("AllSessionsRevoked", new { ActiveSessionCount = count }, () => new { ActiveSessionCount = 0 });
        }, cancellationToken);

    public async Task<CustomerDossierResult> ResetPasswordAsync(ClaimsPrincipal actor, int userN, CustomerPasswordReset request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Credential replacement is an administrative action, never an assistance-context operation.
            if (request.AssistanceSessionId.HasValue) return new(false, Denied);
            if (!AccountEmailRules.ValidPassword(request.NewPassword) ||
                !string.Equals(request.NewPassword, request.ConfirmPassword, StringComparison.Ordinal))
                return new(false, "Въведете еднаква нова парола и потвърждение между 6 и 1024 знака.");
            if (request.ActorPassword?.Length is not (> 0 and <= 1024))
                return new(false, "Въведете текущата парола на Вашия служебен акаунт.");
            // Known secrets must not accidentally become part of the otherwise free-text audit reason.
            if (request.Reason != null && (request.Reason.Contains(request.ActorPassword, StringComparison.Ordinal) ||
                request.Reason.Contains(request.NewPassword!, StringComparison.Ordinal)))
                return new(false, "Основанието не трябва да съдържа пароли.");

            return await ExecuteAsync(actor, userN, request, PermissionCatalog.Users.ResetPassword, async (identity, target, authenticated) =>
            {
                var now = DateTime.UtcNow;
                var since = now.AddMinutes(-15);
                var failedAttempts = await shop.CustomerDossierEvents.AsNoTracking().Where(e =>
                    e.ActorId == authenticated.Id && e.Action == "PasswordResetReauthenticationFailed" && e.OccurredAtUtc >= since)
                    .Take(5).CountAsync(cancellationToken);
                if (failedAttempts >= 5)
                    return Change.Reject("Твърде много неуспешни потвърждения. Изчакайте 15 минути преди нов опит.");
                if (!AccountPasswordCodec.Verify(request.ActorPassword, authenticated.PasswordHash, authenticated.Salt))
                    return Change.AuditReject("PasswordResetReauthenticationFailed",
                        "Текущата парола на Вашия служебен акаунт не е потвърдена.",
                        new { ReauthenticationSucceeded = false });

                var tokens = await identity.AccountEmailTokens.Where(t => t.UserN == target.N &&
                    t.ConsumedAtUtc == null && t.RevokedAtUtc == null).ToListAsync(cancellationToken);
                foreach (var token in tokens) token.RevokedAtUtc = now;
                var tokenIds = tokens.Select(t => t.Id).ToArray();
                var messages = await identity.EmailOutboxMessages.Where(m => m.TokenId != null && tokenIds.Contains(m.TokenId.Value) &&
                    (m.State == EmailOutboxStates.Pending || m.State == EmailOutboxStates.Processing)).ToListAsync(cancellationToken);
                foreach (var message in messages)
                {
                    message.State = EmailOutboxStates.Cancelled;
                    message.ProtectedPayload = null;
                    message.LeaseId = null;
                    message.LeaseUntilUtc = null;
                }
                var credential = AccountPasswordCodec.Create(request.NewPassword!);
                target.PasswordHash = credential.Hash;
                target.Salt = credential.Salt;
                var sessionCount = await InvalidateSessionsAsync(identity, target, cancellationToken);
                // Password replacement neither unlocks nor restores/activates/confirms the account.
                return Change.Apply("PasswordResetByStaff", new { PasswordChanged = false }, () => new
                {
                    PasswordChanged = true, RevokedSessionCount = sessionCount,
                    RevokedTokenCount = tokens.Count, CancelledEmailCount = messages.Count
                });
            }, cancellationToken);
        }
        finally
        {
            request.ActorPassword = request.NewPassword = request.ConfirmPassword = null;
        }
    }

    public async Task<CustomerSessionsSnapshot?> GetSessionsAsync(ClaimsPrincipal actor, int userN, CancellationToken cancellationToken = default)
    {
        if (userN <= 0) return null;
        await using var transaction = await AcquireAsync(cancellationToken);
        await using var identity = CreateIdentity();
        await identity.Database.UseTransactionAsync(transaction.GetDbTransaction(), cancellationToken);
        var authenticated = await FindAuthenticatedAsync(identity, actor, cancellationToken);
        if (authenticated == null || !await HasPermissionAsync(identity, actor, PermissionCatalog.Users.ViewSessions, cancellationToken)) return null;
        var target = await identity.Users.AsNoTracking().SingleOrDefaultAsync(u => u.N == userN, cancellationToken);
        if (target == null || !CustomerManagementRules.CanManage(target) ||
            !await MayManageTargetAsync(identity, authenticated.N, target.N, cancellationToken)) return null;
        var currentKey = actor.FindFirstValue("SessionId");
        var rows = await identity.UserSessions.AsNoTracking().Where(s => s.UserN == userN)
            .OrderByDescending(s => s.IsActive).ThenByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id)
            .Take(200).ToListAsync(cancellationToken);
        return new(CustomerManagementVersions.Account(target), rows.Select(s => new CustomerSessionSummary(
            s.Id, s.CreatedAt, s.DeviceInfo, s.IsActive, authenticated.N == userN && s.SessionId == currentKey,
            CustomerManagementVersions.Session(s))).ToArray());
    }

    private async Task<CustomerDossierResult> ExecuteAsync(ClaimsPrincipal actor, int? userN, CustomerAccountAction request, string permission,
        Func<IdentityDbContext, ApplicationUser, ApplicationUser, Task<Change>> action, CancellationToken cancellationToken, string? operation = null)
    {
        var assistanceId = request.AssistanceSessionId;
        if (!assistanceId.HasValue && (userN is <= 0 || !CustomerManagementVersions.IsVersion(request.ExpectedAccountVersion))) return new(false, Stale);
        if (!assistanceId.HasValue && userN.HasValue && !CustomerManagementRules.ValidReason(request.Reason))
            return new(false, "Въведете основание за промяната до 2000 знака.");
        try
        {
            await using var transaction = await AcquireAsync(cancellationToken);
            await using var identity = CreateIdentity();
            await identity.Database.UseTransactionAsync(transaction.GetDbTransaction(), cancellationToken);
            var authenticated = await FindAuthenticatedAsync(identity, actor, cancellationToken);
            if (authenticated == null) return new(false, Denied);
            CustomerAssistanceContext? assistance = null;
            ApplicationUser? target;
            if (assistanceId.HasValue)
            {
                // Only the four supported profile/address entry points supply a known operation.
                if (operation is not ("ProfileUpdated" or "AddressCreated" or "AddressUpdated" or "AddressDeleted" or "DefaultAddressChanged"))
                    return new(false, Denied);
                assistance = await CustomerAssistanceService.LoadContextAsync(shop, identity, actor, assistanceId.Value, cancellationToken);
                if (assistance == null)
                {
                    await shop.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    return new(false, CustomerAssistanceService.Unavailable);
                }
                var permitted = permission switch
                {
                    PermissionCatalog.Users.EditProfile => assistance.CanEditProfile,
                    PermissionCatalog.Users.ManageAddresses => assistance.CanManageAddresses,
                    _ => false
                };
                if (!permitted)
                {
                    CustomerAssistanceService.AddEvent(shop, assistance.Session, authenticated, "AssistanceAction", operation, "Denied");
                    await shop.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    return new(false, Denied);
                }
                target = assistance.Target;
            }
            else
            {
                if (userN.HasValue && !await HasPermissionAsync(identity, actor, permission, cancellationToken)) return new(false, Denied);
                var targetN = userN ?? authenticated.N;
                target = await identity.Users.FromSqlInterpolated(
                    $"SELECT * FROM [Users] WITH (UPDLOCK,HOLDLOCK) WHERE [N]={targetN}")
                    .SingleOrDefaultAsync(cancellationToken);
                if (target != null && (!CustomerManagementRules.CanManage(target) || (userN.HasValue &&
                    !(permission == PermissionCatalog.Users.ResetPassword
                        ? await MayResetPasswordAsync(identity, authenticated.N, targetN, cancellationToken)
                        : await MayManageTargetAsync(identity, authenticated.N, targetN, cancellationToken))))) return new(false, Denied);
                // Own methods choose the authenticated owner; no posted target or staff flag can bypass this path.
                if (target != null && !userN.HasValue && (!target.IsActive || target.Id != authenticated.Id)) return new(false, Denied);
            }
            if (target == null || !CustomerManagementVersions.IsVersion(request.ExpectedAccountVersion) ||
                CustomerManagementVersions.Account(target) != request.ExpectedAccountVersion)
            {
                if (assistance != null)
                {
                    CustomerAssistanceService.AddEvent(shop, assistance.Session, authenticated, "AssistanceAction", operation!, "Rejected");
                    await shop.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                }
                return new(false, Stale);
            }
            var change = await action(identity, target, authenticated);
            if (change.Action == null)
            {
                if (assistance != null)
                {
                    CustomerAssistanceService.AddEvent(shop, assistance.Session, authenticated, "AssistanceAction", operation!,
                        change.Error != null ? "Rejected" : "NoChange");
                    await shop.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                }
                return new(change.Error == null, change.Error);
            }
            await identity.SaveChangesAsync(cancellationToken);
            var actorName = string.IsNullOrWhiteSpace(authenticated.DisplayName) ? authenticated.UserName : authenticated.DisplayName;
            shop.CustomerDossierEvents.Add(new CustomerDossierEvent
            {
                UserN = target.N, ActorId = authenticated.Id, ActorName = actorName.Length <= 256 ? actorName : actorName[..256],
                OccurredAtUtc = DateTime.UtcNow, Action = change.Action,
                Reason = assistance?.Session.Reason ?? (userN.HasValue ? request.Reason!.Trim() : OwnReason),
                BeforeJson = JsonSerializer.Serialize(change.Before), AfterJson = JsonSerializer.Serialize(change.After!()),
                Note = assistance == null ? null : $"Сесия за съдействие: {assistance.Session.Id:D}"
            });
            if (assistance != null)
                CustomerAssistanceService.AddEvent(shop, assistance.Session, authenticated, "AssistanceAction", operation!, "Succeeded");
            await shop.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(change.Error == null, change.Error);
        }
        catch (DbUpdateException)
        {
            shop.ChangeTracker.Clear();
            return new(false, "Промяната и историята не са записани. Презаредете страницата и опитайте отново.");
        }
        catch (SqlException ex) when (ex.Number is 1205 or 1222 or 51004)
        {
            shop.ChangeTracker.Clear();
            return new(false, "Данните се обработват от друго действие. Презаредете страницата и опитайте отново.");
        }
    }

    private IdentityDbContext CreateIdentity() => new(new DbContextOptionsBuilder<IdentityDbContext>()
        .UseSqlServer(shop.Database.GetDbConnection()).Options);

    private async Task<IDbContextTransaction> AcquireAsync(CancellationToken cancellationToken)
    {
        var transaction = await shop.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await shop.Database.ExecuteSqlRawAsync("""
                DECLARE @result int;
                EXEC @result = sys.sp_getapplock @Resource=N'Algara.AccessManagement', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=15000;
                IF @result < 0 THROW 51004, 'Access management is busy.', 1;
                """, cancellationToken);
            return transaction;
        }
        catch { await transaction.DisposeAsync(); throw; }
    }

    private static async Task<ApplicationUser?> FindAuthenticatedAsync(IdentityDbContext identity, ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var id = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var stamp = principal.FindFirstValue("SecurityStamp");
        var sessionKey = principal.FindFirstValue("SessionId");
        if (principal.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(id) || stamp == null || string.IsNullOrEmpty(sessionKey)) return null;
        var user = await identity.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id && u.IsActive, cancellationToken);
        if (user == null || (user.RequireEmailConfirmation && !user.EmailConfirmed) || user.SecurityStamp != stamp) return null;
        return await identity.UserSessions.AsNoTracking().AnyAsync(s => s.UserN == user.N && s.SessionId == sessionKey && s.IsActive, cancellationToken) ? user : null;
    }

    private static async Task<bool> HasPermissionAsync(IdentityDbContext identity, ClaimsPrincipal actor, string permission, CancellationToken cancellationToken)
    {
        var access = new StaffAccessService(identity);
        var required = new List<string> { PermissionCatalog.Users.View, PermissionCatalog.Users.ViewDossier, permission };
        if (permission == PermissionCatalog.Users.RevokeSessions) required.Add(PermissionCatalog.Users.ViewSessions);
        foreach (var key in required)
            if (!await access.HasPermissionAsync(actor, key, cancellationToken)) return false;
        return true;
    }

    private static async Task<bool> MayManageTargetAsync(IdentityDbContext identity, int actorN, int targetN, CancellationToken cancellationToken)
    {
        if (await identity.UserRoles.AnyAsync(r => r.UserN == actorN && r.Role.Name == StaffRoles.Admin, cancellationToken)) return true;
        var roles = await identity.UserRoles.Where(r => r.UserN == targetN).Select(r => r.Role.Name).ToListAsync(cancellationToken);
        return !roles.Any(StaffRoles.IsStaff);
    }

    private static async Task<bool> MayResetPasswordAsync(IdentityDbContext identity, int actorN, int targetN, CancellationToken cancellationToken)
    {
        if (actorN == targetN) return false;
        var actorRoles = await identity.UserRoles.AsNoTracking().Where(r => r.UserN == actorN).Select(r => r.Role.Name).ToListAsync(cancellationToken);
        var targetRoles = await identity.UserRoles.AsNoTracking().Where(r => r.UserN == targetN).Select(r => r.Role.Name).ToListAsync(cancellationToken);
        return StaffAccountManagementRules.CanResetPassword(actorRoles, targetRoles);
    }

    private static async Task<bool> IsLastAdministratorAsync(IdentityDbContext identity, int userN, CancellationToken cancellationToken) =>
        await identity.UserRoles.AnyAsync(r => r.UserN == userN && r.Role.Name == StaffRoles.Admin && r.User.IsActive, cancellationToken) &&
        !await identity.UserRoles.AnyAsync(r => r.UserN != userN && r.Role.Name == StaffRoles.Admin && r.User.IsActive, cancellationToken);

    private static Task<List<UserAddress>> LoadAddressesAsync(IdentityDbContext identity, int userN, CancellationToken cancellationToken) =>
        identity.UsersAddresses.FromSqlInterpolated(
            $"SELECT * FROM [UsersAddresses] WITH (UPDLOCK,HOLDLOCK) WHERE [UserN]={userN}").ToListAsync(cancellationToken);

    private static async Task<int> InvalidateSessionsAsync(IdentityDbContext identity, ApplicationUser target, CancellationToken cancellationToken)
    {
        target.SecurityStamp = Guid.NewGuid().ToString();
        var sessions = await identity.UserSessions.Where(s => s.UserN == target.N && s.IsActive).ToListAsync(cancellationToken);
        foreach (var session in sessions) session.IsActive = false;
        return sessions.Count;
    }

    private static void EnsureDefault(List<UserAddress> addresses, UserAddress? preferred, DateTime now)
    {
        var chosen = preferred ?? addresses.Where(a => a.IsDefault).OrderBy(a => a.N).FirstOrDefault() ?? addresses.OrderBy(a => a.N).FirstOrDefault();
        foreach (var address in addresses)
        {
            var isDefault = ReferenceEquals(address, chosen);
            if (address.IsDefault != isDefault) { address.IsDefault = isDefault; address.UpdatedAt = now; }
        }
    }

    private static object Profile(ApplicationUser user) => new
    {
        user.FirstName, user.LastName, user.PhoneNumber, user.FullName, user.DisplayName
    };
    private static object Access(ApplicationUser user) => new { user.IsActive, user.AccessSuspendedAtUtc, user.FailedLoginAttempts, user.LockoutUntil };
    private static object Addresses(IEnumerable<UserAddress> addresses) => addresses.OrderBy(a => a.N).Select(a => new
    {
        a.N, a.FirstName, a.LastName, a.PhoneNumber, a.Email, a.AddressLine1, a.AddressLine2,
        a.City, a.PostalCode, a.Country, a.IsDefault
    }).ToArray();
    private static Task<CustomerDossierResult> RejectAsync(string error) => Task.FromResult(new CustomerDossierResult(false, error));
    private sealed record Change(string? Action, object? Before, Func<object>? After, string? Error = null)
    {
        public static Change None => new(null, null, null);
        public static Change Reject(string error) => new(null, null, null, error);
        public static Change AuditReject(string action, string error, object after) => new(action, null, () => after, error);
        public static Change Apply(string action, object before, Func<object> after) => new(action, before, after);
    }
}
