using System.Security.Claims;
using System.Text.Json;
using Algara.Data.Data;
using Algara.Data.Models;
using Algara.Identity.Authorization;
using Algara.Identity.Data;
using Algara.Identity.Models;
using Algara.Web.ViewModels;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Algara.Web.Services;

/// <summary>Staff-owned, tab-specific contexts. Customer authentication and customer sessions are never changed.</summary>
public sealed class CustomerAssistanceService(ShopDbContext shop, ICustomerManagementService management) : ICustomerAssistanceService
{
    internal const string Unavailable = "Съдействието е приключило, изтекло или вече не е разрешено. Отворете досието отново.";
    private const string SaveFailed = "Действието и историята не са записани. Презаредете страницата и опитайте отново.";

    public async Task<CustomerAssistanceStartResult> StartAsync(ClaimsPrincipal actor, int userN, CustomerAssistanceMode mode,
        string? expectedAccountVersion, string? reason, CancellationToken cancellationToken = default)
    {
        if (userN <= 0 || !CustomerAssistanceRules.IsMode(mode) || !CustomerManagementVersions.IsVersion(expectedAccountVersion))
            return new(false, Error: "Презаредете досието и изберете режим за работа.");
        if (!CustomerManagementRules.ValidReason(reason))
            return new(false, Error: "Въведете основание за прегледа или съдействието до 2000 знака.");
        try
        {
            await using var transaction = await AcquireAsync(shop, cancellationToken);
            await using var identity = CreateIdentity(shop);
            await identity.Database.UseTransactionAsync(transaction.GetDbTransaction(), cancellationToken);
            var authenticated = await FindActorAsync(identity, actor, cancellationToken);
            var access = new StaffAccessService(identity);
            if (authenticated == null || !await HasModePermissionAsync(access, actor, mode, cancellationToken))
                return new(false, Error: Unavailable);
            var target = await identity.Users.FromSqlInterpolated(
                $"SELECT * FROM [Users] WITH (UPDLOCK,HOLDLOCK) WHERE [N]={userN}").SingleOrDefaultAsync(cancellationToken);
            if (target == null || CustomerManagementVersions.Account(target) != expectedAccountVersion)
                return new(false, Error: "Акаунтът е променен. Презаредете досието.");
            var roles = await ReadRolesAsync(identity, userN, cancellationToken);
            var actorRoles = await ReadRolesAsync(identity, authenticated.User.N, cancellationToken);
            if (!CustomerAssistanceRules.CanTarget(target, roles, authenticated.User.N, actorRoles))
                return new(false, Error: "Нямате право за съдействие на този акаунт или акаунтът не е активен.");
            var now = DateTime.UtcNow;
            var session = new CustomerAssistanceSession
            {
                Id = Guid.NewGuid(), ActorId = authenticated.User.Id, ActorSessionN = authenticated.SessionN,
                UserN = target.N, Mode = (int)mode, TargetSecurityVersion = CustomerAssistanceRules.SecurityVersion(target, roles),
                Reason = reason!.Trim(), StartedAtUtc = now, ExpiresAtUtc = now + CustomerAssistanceRules.Duration
            };
            shop.CustomerAssistanceSessions.Add(session);
            AddEvent(shop, session, authenticated.User, "AssistanceStarted", "Start", "Succeeded");
            await shop.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(true, session.Id);
        }
        catch (DbUpdateException) { shop.ChangeTracker.Clear(); return new(false, Error: SaveFailed); }
        catch (SqlException ex) when (IsContention(ex)) { shop.ChangeTracker.Clear(); return new(false, Error: SaveFailed); }
    }

    public async Task<CustomerAssistanceSnapshot?> GetAsync(ClaimsPrincipal actor, Guid sessionId, CancellationToken cancellationToken = default)
    {
        if (sessionId == Guid.Empty) return null;
        try
        {
            await using var transaction = await AcquireAsync(shop, cancellationToken);
            await using var identity = CreateIdentity(shop);
            await identity.Database.UseTransactionAsync(transaction.GetDbTransaction(), cancellationToken);
            var context = await LoadContextAsync(shop, identity, actor, sessionId, cancellationToken);
            CustomerAssistanceSnapshot? snapshot = null;
            if (context != null)
            {
                snapshot = await BuildSnapshotAsync(identity, context, cancellationToken);
                AddEvent(shop, context.Session, context.Actor, "AssistanceViewed", "ViewProfile", "Succeeded");
            }
            await shop.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return snapshot;
        }
        catch (DbUpdateException) { shop.ChangeTracker.Clear(); return null; }
        catch (SqlException ex) when (IsContention(ex)) { shop.ChangeTracker.Clear(); return null; }
    }

    public async Task<CustomerAssistanceOrderSnapshot?> GetOrderAsync(ClaimsPrincipal actor, Guid sessionId, int orderN,
        CancellationToken cancellationToken = default)
    {
        if (sessionId == Guid.Empty) return null;
        try
        {
            await using var transaction = await AcquireAsync(shop, cancellationToken);
            await using var identity = CreateIdentity(shop);
            await identity.Database.UseTransactionAsync(transaction.GetDbTransaction(), cancellationToken);
            var context = await LoadContextAsync(shop, identity, actor, sessionId, cancellationToken);
            CustomerAssistanceOrderSnapshot? result = null;
            if (context != null)
            {
                var order = context.CanViewOrders && orderN > 0
                    ? await shop.Orders.AsNoTracking().Include(o => o.OrderItems).ThenInclude(i => i.Product)
                        .SingleOrDefaultAsync(o => o.N == orderN && o.UserN == context.Target.N, cancellationToken)
                    : null;
                if (order != null)
                {
                    result = new(await BuildSnapshotAsync(identity, context, cancellationToken), new()
                    {
                        Order = order, StatusLabel = StatusLabel(order.Status), StatusCssClass = StatusCssClass(order.Status)
                    });
                }
                // A denied/foreign order number is not copied to the customer's history.
                AddEvent(shop, context.Session, context.Actor, "AssistanceOrderViewed", "ViewOrder",
                    order == null ? "Rejected" : "Succeeded", order?.N);
            }
            await shop.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (DbUpdateException) { shop.ChangeTracker.Clear(); return null; }
        catch (SqlException ex) when (IsContention(ex)) { shop.ChangeTracker.Clear(); return null; }
    }

    public async Task<CustomerDossierResult> EndAsync(ClaimsPrincipal actor, Guid sessionId, CancellationToken cancellationToken = default)
    {
        if (sessionId == Guid.Empty) return new(false, Unavailable);
        try
        {
            await using var transaction = await AcquireAsync(shop, cancellationToken);
            await using var identity = CreateIdentity(shop);
            await identity.Database.UseTransactionAsync(transaction.GetDbTransaction(), cancellationToken);
            var authenticated = await FindActorAsync(identity, actor, cancellationToken);
            if (authenticated == null) return new(false, Unavailable);
            var session = await FindOwnedSessionAsync(shop, sessionId, authenticated, cancellationToken);
            if (session == null) return new(false, Unavailable);
            if (session.EndedAtUtc == null)
            {
                var now = DateTime.UtcNow;
                Close(shop, session, authenticated.User, now, now >= session.ExpiresAtUtc ? "Expired" : "StaffEnded");
                await shop.SaveChangesAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
            return new(true);
        }
        catch (DbUpdateException) { shop.ChangeTracker.Clear(); return new(false, SaveFailed); }
        catch (SqlException ex) when (IsContention(ex)) { shop.ChangeTracker.Clear(); return new(false, SaveFailed); }
    }

    public async Task<CustomerDossierResult> RecordValidationFailureAsync(ClaimsPrincipal actor, Guid sessionId, string operation,
        CancellationToken cancellationToken = default)
    {
        if (sessionId == Guid.Empty || operation is not ("ProfileUpdated" or "AddressCreated" or "AddressUpdated" or "AddressDeleted" or "DefaultAddressChanged"))
            return new(false, Unavailable);
        try
        {
            await using var transaction = await AcquireAsync(shop, cancellationToken);
            await using var identity = CreateIdentity(shop);
            await identity.Database.UseTransactionAsync(transaction.GetDbTransaction(), cancellationToken);
            var context = await LoadContextAsync(shop, identity, actor, sessionId, cancellationToken);
            if (context != null)
            {
                var permitted = operation == "ProfileUpdated" ? context.CanEditProfile : context.CanManageAddresses;
                AddEvent(shop, context.Session, context.Actor, "AssistanceAction", operation, permitted ? "ValidationRejected" : "Denied");
            }
            await shop.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return context == null ? new(false, Unavailable) : new(true);
        }
        catch (DbUpdateException) { shop.ChangeTracker.Clear(); return new(false, SaveFailed); }
        catch (SqlException ex) when (IsContention(ex)) { shop.ChangeTracker.Clear(); return new(false, SaveFailed); }
    }

    public Task<CustomerDossierResult> EditProfileAsync(ClaimsPrincipal actor, Guid sessionId, CustomerProfileUpdate request,
        CancellationToken cancellationToken = default) => management.EditOwnProfileAsync(actor, new CustomerProfileUpdate
        {
            AssistanceSessionId = sessionId, ExpectedAccountVersion = request.ExpectedAccountVersion,
            FirstName = request.FirstName, LastName = request.LastName, PhoneNumber = request.PhoneNumber
        }, cancellationToken);

    public Task<CustomerDossierResult> SaveAddressAsync(ClaimsPrincipal actor, Guid sessionId, CustomerAddressUpdate request,
        CancellationToken cancellationToken = default) => management.SaveOwnAddressAsync(actor, new CustomerAddressUpdate
        {
            AssistanceSessionId = sessionId, ExpectedAccountVersion = request.ExpectedAccountVersion,
            ExpectedAddressesVersion = request.ExpectedAddressesVersion, N = request.N,
            FirstName = request.FirstName, LastName = request.LastName, PhoneNumber = request.PhoneNumber,
            Email = request.Email, AddressLine1 = request.AddressLine1, AddressLine2 = request.AddressLine2,
            City = request.City, PostalCode = request.PostalCode, Country = request.Country, IsDefault = request.IsDefault
        }, cancellationToken);

    public Task<CustomerDossierResult> DeleteAddressAsync(ClaimsPrincipal actor, Guid sessionId, CustomerAddressAction request,
        CancellationToken cancellationToken = default) => management.DeleteOwnAddressAsync(actor, AddressAction(sessionId, request), cancellationToken);

    public Task<CustomerDossierResult> SetDefaultAddressAsync(ClaimsPrincipal actor, Guid sessionId, CustomerAddressAction request,
        CancellationToken cancellationToken = default) => management.SetDefaultOwnAddressAsync(actor, AddressAction(sessionId, request), cancellationToken);

    private static CustomerAddressAction AddressAction(Guid sessionId, CustomerAddressAction request) => new()
    {
        AssistanceSessionId = sessionId, AddressN = request.AddressN, ExpectedAccountVersion = request.ExpectedAccountVersion,
        ExpectedAddressesVersion = request.ExpectedAddressesVersion
    };

    // Caller owns the shared AccessManagement lock/transaction. Invalid owned contexts are ended atomically by its caller.
    internal static async Task<CustomerAssistanceContext?> LoadContextAsync(ShopDbContext shop, IdentityDbContext identity,
        ClaimsPrincipal actor, Guid sessionId, CancellationToken cancellationToken)
    {
        if (sessionId == Guid.Empty) return null;
        var authenticated = await FindActorAsync(identity, actor, cancellationToken);
        if (authenticated == null) return null;
        var session = await FindOwnedSessionAsync(shop, sessionId, authenticated, cancellationToken);
        if (session == null || session.EndedAtUtc != null) return null;
        var now = DateTime.UtcNow;
        if (!CustomerAssistanceRules.IsActive(session, now))
        {
            Close(shop, session, authenticated.User, now, now >= session.ExpiresAtUtc ? "Expired" : "InvalidContext");
            return null;
        }
        var access = new StaffAccessService(identity);
        if (!await HasModePermissionAsync(access, actor, (CustomerAssistanceMode)session.Mode, cancellationToken))
        {
            Close(shop, session, authenticated.User, now, "AccessChanged");
            return null;
        }
        var target = await identity.Users.FromSqlInterpolated(
            $"SELECT * FROM [Users] WITH (UPDLOCK,HOLDLOCK) WHERE [N]={session.UserN}").SingleOrDefaultAsync(cancellationToken);
        var roles = await ReadRolesAsync(identity, session.UserN, cancellationToken);
        if (target == null || CustomerAssistanceRules.SecurityVersion(target, roles) != session.TargetSecurityVersion)
        {
            Close(shop, session, authenticated.User, now, "TargetChanged");
            return null;
        }
        // Read actual roles inside this transaction. Cookie role claims cannot retain a former hierarchy grant.
        var actorRoles = await ReadRolesAsync(identity, authenticated.User.N, cancellationToken);
        if (!CustomerAssistanceRules.CanTarget(target, roles, authenticated.User.N, actorRoles))
        {
            Close(shop, session, authenticated.User, now, "AccessChanged");
            return null;
        }
        var canWrite = session.Mode == (int)CustomerAssistanceMode.Assist;
        return new(session, authenticated.User, target,
            canWrite && await access.HasPermissionAsync(actor, PermissionCatalog.Users.EditProfile, cancellationToken),
            canWrite && await access.HasPermissionAsync(actor, PermissionCatalog.Users.ManageAddresses, cancellationToken),
            await access.HasPermissionAsync(actor, PermissionCatalog.Orders.View, cancellationToken));
    }

    internal static void AddEvent(ShopDbContext shop, CustomerAssistanceSession session, ApplicationUser actor,
        string action, string operation, string result, int? orderN = null)
    {
        var name = string.IsNullOrWhiteSpace(actor.DisplayName) ? actor.UserName : actor.DisplayName;
        shop.CustomerDossierEvents.Add(new CustomerDossierEvent
        {
            UserN = session.UserN, ActorId = actor.Id, ActorName = name.Length <= 256 ? name : name[..256],
            OccurredAtUtc = DateTime.UtcNow, Action = action, Reason = session.Reason,
            AfterJson = JsonSerializer.Serialize(new
            {
                SessionId = session.Id, Mode = (CustomerAssistanceMode)session.Mode == CustomerAssistanceMode.Preview ? "Preview" : "Assist",
                Operation = operation, Result = result, session.StartedAtUtc, session.ExpiresAtUtc,
                session.EndedAtUtc, session.EndReason, OrderN = orderN
            })
        });
    }

    private static void Close(ShopDbContext shop, CustomerAssistanceSession session, ApplicationUser actor, DateTime now, string reason)
    {
        if (session.EndedAtUtc != null) return;
        session.EndedAtUtc = now >= session.ExpiresAtUtc ? session.ExpiresAtUtc : now < session.StartedAtUtc ? session.StartedAtUtc : now;
        session.EndReason = reason;
        AddEvent(shop, session, actor, "AssistanceEnded", "End", reason);
    }

    private static Task<List<string>> ReadRolesAsync(IdentityDbContext identity, int userN, CancellationToken cancellationToken) =>
        identity.UserRoles.AsNoTracking().Where(r => r.UserN == userN).Select(r => r.Role.Name).ToListAsync(cancellationToken);

    private static async Task<bool> HasModePermissionAsync(StaffAccessService access, ClaimsPrincipal actor,
        CustomerAssistanceMode mode, CancellationToken cancellationToken) =>
        CustomerAssistanceRules.IsMode(mode) &&
        await access.HasPermissionAsync(actor, PermissionCatalog.Users.View, cancellationToken) &&
        await access.HasPermissionAsync(actor, PermissionCatalog.Users.ViewDossier, cancellationToken) &&
        await access.HasPermissionAsync(actor, mode == CustomerAssistanceMode.Preview
            ? PermissionCatalog.Users.PreviewCustomer : PermissionCatalog.Users.AssistCustomer, cancellationToken);

    private static async Task<AuthenticatedActor?> FindActorAsync(IdentityDbContext identity, ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var id = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var stamp = principal.FindFirstValue("SecurityStamp");
        var sessionKey = principal.FindFirstValue("SessionId");
        if (principal.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(id) || stamp == null || string.IsNullOrEmpty(sessionKey)) return null;
        var user = await identity.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id && u.IsActive, cancellationToken);
        if (user == null || (user.RequireEmailConfirmation && !user.EmailConfirmed) || !string.Equals(user.SecurityStamp, stamp, StringComparison.Ordinal)) return null;
        var sessionN = await identity.UserSessions.AsNoTracking().Where(s => s.UserN == user.N && s.SessionId == sessionKey && s.IsActive)
            .Select(s => (int?)s.Id).SingleOrDefaultAsync(cancellationToken);
        return sessionN.HasValue ? new(user, sessionN.Value) : null;
    }

    private static async Task<CustomerAssistanceSession?> FindOwnedSessionAsync(ShopDbContext shop, Guid sessionId,
        AuthenticatedActor actor, CancellationToken cancellationToken)
    {
        var session = await shop.CustomerAssistanceSessions.FromSqlInterpolated(
            $"SELECT * FROM [CustomerAssistanceSessions] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]={sessionId} AND [ActorId]={actor.User.Id} AND [ActorSessionN]={actor.SessionN}")
            .SingleOrDefaultAsync(cancellationToken);
        // One request can read after a mutation. EF identity resolution must not retain a context ended by another request.
        if (session != null) await shop.Entry(session).ReloadAsync(cancellationToken);
        return session != null && session.ActorId == actor.User.Id && session.ActorSessionN == actor.SessionN ? session : null;
    }

    private async Task<CustomerAssistanceSnapshot> BuildSnapshotAsync(IdentityDbContext identity, CustomerAssistanceContext context, CancellationToken cancellationToken)
    {
        var user = context.Target;
        var firstName = user.FirstName?.Trim() ?? string.Empty;
        var lastName = user.LastName?.Trim() ?? string.Empty;
        if (firstName.Length == 0 && lastName.Length == 0 && !string.IsNullOrWhiteSpace(user.FullName) &&
            !string.Equals(user.FullName, user.Email, StringComparison.OrdinalIgnoreCase) && !string.Equals(user.FullName, user.UserName, StringComparison.OrdinalIgnoreCase))
        {
            var names = user.FullName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            firstName = names.ElementAtOrDefault(0) ?? string.Empty;
            lastName = names.ElementAtOrDefault(1) ?? string.Empty;
        }
        var addresses = await identity.UsersAddresses.AsNoTracking().Where(a => a.UserN == user.N)
            .OrderByDescending(a => a.IsDefault).ThenByDescending(a => a.UpdatedAt ?? a.CreatedAt).ToListAsync(cancellationToken);
        var accountVersion = CustomerManagementVersions.Account(user);
        var addressesVersion = CustomerManagementVersions.AddressBook(addresses);
        var orders = context.CanViewOrders
            ? await shop.Orders.AsNoTracking().Where(o => o.UserN == user.N).OrderByDescending(o => o.CreatedAt)
                .Select(o => new ProfileOrderRowViewModel
                {
                    N = o.N, Status = o.Status, TotalAmount = o.TotalAmount, CreatedAt = o.CreatedAt,
                    UpdatedAt = o.UpdatedAt, ItemCount = o.OrderItems.Sum(i => i.Quantity)
                }).ToListAsync(cancellationToken)
            : [];
        foreach (var order in orders) { order.StatusLabel = StatusLabel(order.Status); order.StatusCssClass = StatusCssClass(order.Status); }
        return new()
        {
            SessionId = context.Session.Id, UserN = user.N,
            CustomerName = string.IsNullOrWhiteSpace(user.DisplayName) ? user.Email : user.DisplayName,
            Mode = (CustomerAssistanceMode)context.Session.Mode, Reason = context.Session.Reason,
            StartedAtUtc = context.Session.StartedAtUtc, ExpiresAtUtc = context.Session.ExpiresAtUtc,
            CanEditProfile = context.CanEditProfile, CanManageAddresses = context.CanManageAddresses, CanViewOrders = context.CanViewOrders,
            Profile = new()
            {
                AccountVersion = accountVersion, AddressBookVersion = addressesVersion,
                Details = new() { ExpectedVersion = accountVersion, FirstName = firstName, LastName = lastName, PhoneNumber = user.PhoneNumber, Email = user.Email },
                Address = new()
                {
                    ExpectedAccountVersion = accountVersion, ExpectedVersion = addressesVersion,
                    FirstName = firstName, LastName = lastName, PhoneNumber = user.PhoneNumber, Email = user.Email, Country = "България"
                },
                Addresses = addresses.Select(a => new ProfileAddressViewModel
                {
                    N = a.N, FirstName = a.FirstName, LastName = a.LastName, PhoneNumber = a.PhoneNumber,
                    Email = a.Email, AddressLine1 = a.AddressLine1, AddressLine2 = a.AddressLine2,
                    City = a.City, PostalCode = a.PostalCode, Country = a.Country, IsDefault = a.IsDefault
                }).ToArray(),
                CurrentOrders = orders.Where(o => o.Status is OrderStatus.Pending or OrderStatus.Confirmed or OrderStatus.Shipped).ToArray(),
                OrderHistory = orders.Where(o => o.Status is OrderStatus.Delivered or OrderStatus.Cancelled).ToArray()
            }
        };
    }

    private static string StatusLabel(OrderStatus status) => status switch
    {
        OrderStatus.Pending => "Чакаща", OrderStatus.Confirmed => "Потвърдена", OrderStatus.Shipped => "Изпратена",
        OrderStatus.Delivered => "Доставена", OrderStatus.Cancelled => "Отказана", _ => status.ToString()
    };
    private static string StatusCssClass(OrderStatus status) => status switch
    {
        OrderStatus.Pending => "bg-warning text-dark", OrderStatus.Confirmed => "bg-primary", OrderStatus.Shipped => "bg-info text-dark",
        OrderStatus.Delivered => "bg-success", OrderStatus.Cancelled => "bg-secondary", _ => "bg-light text-dark"
    };
    private static IdentityDbContext CreateIdentity(ShopDbContext shop) => new(new DbContextOptionsBuilder<IdentityDbContext>()
        .UseSqlServer(shop.Database.GetDbConnection()).Options);

    private static async Task<IDbContextTransaction> AcquireAsync(ShopDbContext shop, CancellationToken cancellationToken)
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
    private static bool IsContention(SqlException ex) => ex.Number is 1205 or 1222 or 51004;
    private sealed record AuthenticatedActor(ApplicationUser User, int SessionN);
}

internal sealed record CustomerAssistanceContext(CustomerAssistanceSession Session, ApplicationUser Actor, ApplicationUser Target,
    bool CanEditProfile, bool CanManageAddresses, bool CanViewOrders);
