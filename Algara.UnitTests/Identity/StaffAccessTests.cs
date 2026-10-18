using System.Security.Claims;
using Algara.Identity.Authorization;
using Algara.Identity.Data;
using Microsoft.EntityFrameworkCore;
using Algara.UnitTests.Controllers;

namespace Algara.UnitTests.Identity;

public sealed class StaffAccessTests
{
    [Fact]
    public async Task Moderator_ReceivesOnlyStoredOrdinaryGrantsEvenWithAdminCookieOrRogueRows()
    {
        using var db = new StaffAccessTestContext(StaffRoles.Moderator,
            [PermissionCatalog.Products.View, PermissionCatalog.Access.AssignRoles, "Unknown.Right"]);
        var principal = StaffAccessTestContext.Principal();
        ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim(ClaimTypes.Role, StaffRoles.Admin));
        var service = new StaffAccessService(db);
        Assert.True(await service.IsStaffAsync(principal));
        Assert.True(await service.HasPermissionAsync(principal, PermissionCatalog.Products.View));
        Assert.False(await service.HasPermissionAsync(principal, PermissionCatalog.Products.Edit));
        Assert.False(await service.HasPermissionAsync(principal, PermissionCatalog.Access.AssignRoles));
        Assert.False(await service.HasPermissionAsync(principal, "Unknown.Right"));
    }

    [Fact]
    public async Task GrantRevocation_IsObservedByNextRequestWithoutChangingTheCookie()
    {
        using var db = new StaffAccessTestContext(StaffRoles.Moderator, [PermissionCatalog.Products.View]);
        var principal = StaffAccessTestContext.Principal();
        var firstRequest = new StaffAccessService(db);
        Assert.True(await firstRequest.HasPermissionAsync(principal, PermissionCatalog.Products.View));
        db.Grants.Clear();
        Assert.True(await firstRequest.HasPermissionAsync(principal, PermissionCatalog.Products.View));
        Assert.False(await new StaffAccessService(db).HasPermissionAsync(principal, PermissionCatalog.Products.View));
    }

    [Fact]
    public async Task MultipleStaffRoles_CombineTheirOrdinaryGrants()
    {
        using var db = new StaffAccessTestContext(StaffRoles.CatalogEditor, [PermissionCatalog.Products.View]);
        var role = new ApplicationRole { N = 2, Name = StaffRoles.OrderManager };
        db.Assignments.Add(new UserRole { UserN = db.PrimaryUser.N, User = db.PrimaryUser, RoleN = role.N, Role = role });
        db.Grants.Add(new RolePermission { RoleN = role.N, Role = role, PermissionKey = PermissionCatalog.Orders.View });
        var service = new StaffAccessService(db);
        Assert.True(await service.HasPermissionAsync(StaffAccessTestContext.Principal(), PermissionCatalog.Products.View));
        Assert.True(await service.HasPermissionAsync(StaffAccessTestContext.Principal(), PermissionCatalog.Orders.View));
        Assert.False(await service.HasPermissionAsync(StaffAccessTestContext.Principal(), PermissionCatalog.Access.ManagePermissions));
    }

    [Fact]
    public async Task RoleRevocation_IsObservedByNextRequestEvenIfCookieContainsAdminRole()
    {
        using var db = new StaffAccessTestContext();
        var principal = StaffAccessTestContext.Principal();
        ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim(ClaimTypes.Role, StaffRoles.Admin));
        Assert.True(await new StaffAccessService(db).HasPermissionAsync(principal, PermissionCatalog.Access.AssignRoles));
        db.Assignments.RemoveAll(r => r.UserN == db.PrimaryUser.N);
        Assert.False(await new StaffAccessService(db).HasPermissionAsync(principal, PermissionCatalog.Access.AssignRoles));
    }

    [Theory]
    [InlineData("User")]
    [InlineData("Unknown")]
    public async Task NonstaffRoles_NeverGainPermissionsFromStoredGrantRows(string role)
    {
        using var db = new StaffAccessTestContext(role, [PermissionCatalog.Products.View]);
        var service = new StaffAccessService(db);
        Assert.False(await service.IsStaffAsync(StaffAccessTestContext.Principal()));
        Assert.False(await service.HasPermissionAsync(StaffAccessTestContext.Principal(), PermissionCatalog.Products.View));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task InactiveAccountOrSession_CannotUseExistingStaffMembership(bool active, bool sessionActive)
    {
        using var db = new StaffAccessTestContext(active: active, sessionActive: sessionActive);
        Assert.False(await new StaffAccessService(db).IsStaffAsync(StaffAccessTestContext.Principal()));
    }

    [Fact]
    public async Task ChangedSecurityStamp_CannotUseExistingStaffMembership()
    {
        using var db = new StaffAccessTestContext();
        db.PrimaryUser.SecurityStamp = "changed-stamp";
        Assert.False(await new StaffAccessService(db).IsStaffAsync(StaffAccessTestContext.Principal()));
    }

    [Fact]
    public async Task LegacyEmptyStamp_ActiveAdministratorReceivesAllKnownPermissions()
    {
        using var db = new StaffAccessTestContext();
        db.PrimaryUser.SecurityStamp = string.Empty;
        var principal = LegacyPrincipal();
        var service = new StaffAccessService(db);

        Assert.True(await service.IsStaffAsync(principal));
        foreach (var permission in PermissionCatalog.All)
            Assert.True(await service.HasPermissionAsync(principal, permission.Key), permission.Key);
        Assert.False(await service.HasPermissionAsync(principal, "Unknown.Right"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyEmptyStamp_AndMissingStampClaimHaveDistinctCachedSnapshots(bool missingFirst)
    {
        using var db = new StaffAccessTestContext();
        db.PrimaryUser.SecurityStamp = string.Empty;
        var legacy = LegacyPrincipal();
        var missing = LegacyPrincipal(includeStamp: false);
        var service = new StaffAccessService(db);

        Assert.Equal(!missingFirst, await service.IsStaffAsync(missingFirst ? missing : legacy));
        Assert.Equal(missingFirst, await service.IsStaffAsync(missingFirst ? legacy : missing));
        Assert.False(await service.HasPermissionAsync(missing, PermissionCatalog.Access.AssignRoles));
        Assert.True(await service.HasPermissionAsync(legacy, PermissionCatalog.Access.AssignRoles));
    }

    [Fact]
    public async Task LegacyEmptyStamp_IsInvalidatedWhenDatabaseStampChanges()
    {
        using var db = new StaffAccessTestContext();
        db.PrimaryUser.SecurityStamp = string.Empty;
        var principal = LegacyPrincipal();
        Assert.True(await new StaffAccessService(db).IsStaffAsync(principal));

        db.PrimaryUser.SecurityStamp = "new-stamp-after-role-change";

        var nextRequest = new StaffAccessService(db);
        Assert.False(await nextRequest.IsStaffAsync(principal));
        Assert.False(await nextRequest.HasPermissionAsync(principal, PermissionCatalog.Access.AssignRoles));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task LegacyEmptyStamp_CannotBypassInactiveAccountOrSession(bool active, bool sessionActive)
    {
        using var db = new StaffAccessTestContext(active: active, sessionActive: sessionActive);
        db.PrimaryUser.SecurityStamp = string.Empty;
        var principal = LegacyPrincipal();
        var service = new StaffAccessService(db);

        Assert.False(await service.IsStaffAsync(principal));
        Assert.False(await service.HasPermissionAsync(principal, PermissionCatalog.Access.AssignRoles));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown-session")]
    [InlineData("session-other-admin")]
    public async Task LegacyEmptyStamp_RequiresOwnActiveSession(string? session)
    {
        using var db = new StaffAccessTestContext();
        db.PrimaryUser.SecurityStamp = string.Empty;
        var principal = LegacyPrincipal();
        var identity = (ClaimsIdentity)principal.Identity!;
        identity.RemoveClaim(identity.FindFirst("SessionId")!);
        if (session != null) identity.AddClaim(new Claim("SessionId", session));
        var service = new StaffAccessService(db);

        Assert.False(await service.IsStaffAsync(principal));
        Assert.False(await service.HasPermissionAsync(principal, PermissionCatalog.Access.AssignRoles));
    }

    [Fact]
    public async Task LegacyEmptyStamp_CannotPreserveRevokedAdministratorRoleFromCookie()
    {
        using var db = new StaffAccessTestContext();
        db.PrimaryUser.SecurityStamp = string.Empty;
        var principal = LegacyPrincipal();
        ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim(ClaimTypes.Role, StaffRoles.Admin));
        Assert.True(await new StaffAccessService(db).IsStaffAsync(principal));

        db.Assignments.RemoveAll(r => r.UserN == db.PrimaryUser.N);

        var nextRequest = new StaffAccessService(db);
        Assert.False(await nextRequest.IsStaffAsync(principal));
        Assert.False(await nextRequest.HasPermissionAsync(principal, PermissionCatalog.Access.AssignRoles));
    }

    [Theory]
    [InlineData("User", false)]
    [InlineData("Unknown", false)]
    [InlineData("Moderator", true)]
    public async Task LegacyEmptyStamp_DoesNotElevateActualRoleFromAdministratorCookie(string role, bool staff)
    {
        using var db = new StaffAccessTestContext(role, [PermissionCatalog.Products.View, PermissionCatalog.Access.AssignRoles]);
        db.PrimaryUser.SecurityStamp = string.Empty;
        var principal = LegacyPrincipal();
        ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim(ClaimTypes.Role, StaffRoles.Admin));
        var service = new StaffAccessService(db);

        Assert.Equal(staff, await service.IsStaffAsync(principal));
        Assert.Equal(staff, await service.HasPermissionAsync(principal, PermissionCatalog.Products.View));
        Assert.False(await service.HasPermissionAsync(principal, PermissionCatalog.Access.AssignRoles));
        Assert.False(await service.HasPermissionAsync(principal, PermissionCatalog.Access.ManagePermissions));
        Assert.False(await service.HasPermissionAsync(principal, PermissionCatalog.Settings.Manage));
    }

    private static ClaimsPrincipal LegacyPrincipal(bool includeStamp = true)
    {
        var principal = StaffAccessTestContext.Principal();
        var identity = (ClaimsIdentity)principal.Identity!;
        identity.RemoveClaim(identity.FindFirst("SecurityStamp")!);
        if (includeStamp) identity.AddClaim(new Claim("SecurityStamp", string.Empty));
        return principal;
    }

    [Fact]
    public void Defaults_KeepCriticalRightsWithAdministratorAndCustomersOutsideMatrix()
    {
        var critical = PermissionCatalog.All.Where(p => p.AdminOnly).Select(p => p.Key).ToArray();
        Assert.Equal(3, critical.Length);
        Assert.Contains(PermissionCatalog.Access.AssignRoles, critical);
        Assert.Contains(PermissionCatalog.Access.ManagePermissions, critical);
        Assert.Contains(PermissionCatalog.Settings.Manage, critical);
        Assert.Empty(PermissionCatalog.DefaultGrants("User"));
        Assert.Empty(PermissionCatalog.DefaultGrants("Unknown"));
        foreach (var role in StaffRoles.All.Where(r => r != StaffRoles.Admin))
            Assert.Empty(PermissionCatalog.DefaultGrants(role).Intersect(critical));
        Assert.Equal(PermissionCatalog.All.Count, PermissionCatalog.DefaultGrants(StaffRoles.Admin).Count);
    }

    [Fact]
    public void Defaults_SeparateCatalogStaffFromCustomerData()
    {
        var catalog = PermissionCatalog.DefaultGrants(StaffRoles.CatalogEditor);
        Assert.Contains(PermissionCatalog.Products.Edit, catalog);
        Assert.DoesNotContain(PermissionCatalog.Users.View, catalog);
        Assert.DoesNotContain(PermissionCatalog.Partners.View, catalog);
        var orders = PermissionCatalog.DefaultGrants(StaffRoles.OrderManager);
        Assert.Contains(PermissionCatalog.Orders.ChangeStatus, orders);
        Assert.Contains(PermissionCatalog.Partners.View, orders);
        Assert.DoesNotContain(PermissionCatalog.Products.ChangePrice, orders);
        Assert.DoesNotContain(PermissionCatalog.Access.ViewHistory, orders);
    }

    [Fact]
    public void Catalog_HasUniqueStableKeysAndEveryDefaultActionHasViewPermission()
    {
        Assert.Equal(PermissionCatalog.All.Count, PermissionCatalog.All.Select(p => p.Key).Distinct(StringComparer.Ordinal).Count());
        foreach (var role in StaffRoles.All)
        {
            var grants = PermissionCatalog.DefaultGrants(role);
            foreach (var grant in grants)
            {
                var view = grant.Split('.')[0] + ".View";
                if (PermissionCatalog.Find(view) != null) Assert.Contains(view, grants);
            }
            if (grants.Contains(PermissionCatalog.Products.Create)) Assert.Contains(PermissionCatalog.Products.ChangePrice, grants);
            if (grants.Any(k => k.StartsWith("PartnerGroups.", StringComparison.Ordinal))) Assert.Contains(PermissionCatalog.Partners.View, grants);
        }
    }

    [Theory]
    [InlineData(false, true, true, true)]
    [InlineData(true, false, true, true)]
    [InlineData(true, true, false, true)]
    [InlineData(true, true, true, false)]
    public async Task IncompleteIdentity_CannotGainStaffPermissionsFromRoleClaims(bool authenticated, bool id, bool stamp, bool session)
    {
        using var db = new IdentityDbContext(new DbContextOptions<IdentityDbContext>());
        var claims = new List<Claim> { new(ClaimTypes.Role, "Admin") };
        if (id) claims.Add(new(ClaimTypes.NameIdentifier, "user-id"));
        if (stamp) claims.Add(new("SecurityStamp", "stamp"));
        if (session) claims.Add(new("SessionId", "session"));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticated ? "Cookies" : null));
        var service = new StaffAccessService(db);
        Assert.False(await service.IsStaffAsync(principal));
        Assert.False(await service.HasPermissionAsync(principal, PermissionCatalog.Access.AssignRoles));
    }

    [Fact]
    public async Task UnknownPermission_IsDeniedBeforeDatabaseAccess()
    {
        using var db = new IdentityDbContext(new DbContextOptions<IdentityDbContext>());
        Assert.False(await new StaffAccessService(db).HasPermissionAsync(new ClaimsPrincipal(), "Future.UnregisteredAction"));
    }

    [Theory]
    [InlineData("Admin", "Products.View")]
    [InlineData("User", "Products.View")]
    [InlineData("Unknown", "Products.View")]
    [InlineData("Moderator", "Access.AssignRoles")]
    [InlineData("Moderator", "Access.ManagePermissions")]
    [InlineData("Moderator", "Settings.Manage")]
    [InlineData("Moderator", "Unknown.Right")]
    [InlineData("Moderator", "Products.Edit")]
    public async Task InvalidMatrix_IsRejectedBeforeDatabaseAccess(string role, string key)
    {
        using var db = new IdentityDbContext(new DbContextOptions<IdentityDbContext>());
        var result = await new AccessManagementService(db).SaveRolePermissionsAsync(new ClaimsPrincipal(), role, [key], 0);
        Assert.False(result.Succeeded);
        Assert.False(result.Changed);
        Assert.NotEmpty(result.Error!);
    }

    [Fact]
    public async Task ExcessiveMatrix_IsRejectedBeforeDatabaseAccess()
    {
        using var db = new IdentityDbContext(new DbContextOptions<IdentityDbContext>());
        var result = await new AccessManagementService(db).SaveRolePermissionsAsync(new ClaimsPrincipal(), StaffRoles.Moderator,
            Enumerable.Repeat(PermissionCatalog.Products.View, PermissionCatalog.All.Count + 1), 0);
        Assert.False(result.Succeeded);
    }

    [Theory]
    [InlineData("Products.View", "Products.Create")]
    [InlineData("PartnerGroups.View")]
    [InlineData("PartnerGroups.View", "PartnerGroups.Edit")]
    public async Task MissingDependentPermission_IsRejectedBeforeDatabaseAccess(params string[] keys)
    {
        using var db = new IdentityDbContext(new DbContextOptions<IdentityDbContext>());
        var result = await new AccessManagementService(db).SaveRolePermissionsAsync(new ClaimsPrincipal(), StaffRoles.Moderator, keys, 0);
        Assert.False(result.Succeeded);
    }

    [Theory]
    [InlineData("Unknown", "stamp")]
    [InlineData("Admin", null)]
    public async Task InvalidRoleChange_IsRejectedBeforeDatabaseAccess(string role, string? stamp)
    {
        using var db = new IdentityDbContext(new DbContextOptions<IdentityDbContext>());
        var result = await new AccessManagementService(db).SetUserRoleAsync(new ClaimsPrincipal(), "target-id", role, true, stamp);
        Assert.False(result.Succeeded);
    }
}
