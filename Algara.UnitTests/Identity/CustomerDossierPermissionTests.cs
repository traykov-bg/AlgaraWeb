using System.Security.Claims;
using System.Reflection;
using Algara.Identity.Authorization;
using Algara.Identity.Data;
using Algara.UnitTests.Controllers;
using Algara.Web.Authorization;
using Algara.Web.Controllers;
using Microsoft.EntityFrameworkCore;

namespace Algara.UnitTests.Identity;

public sealed class CustomerDossierPermissionTests
{
    public static TheoryData<string> DossierRights => new()
    {
        PermissionCatalog.Users.ViewDossier,
        PermissionCatalog.Users.AddNotes,
        PermissionCatalog.Users.ManagePartnerLinks,
        PermissionCatalog.Users.ViewDossierHistory,
        PermissionCatalog.Users.EditProfile,
        PermissionCatalog.Users.ManageAddresses,
        PermissionCatalog.Users.Unlock,
        PermissionCatalog.Users.SuspendAccess,
        PermissionCatalog.Users.ViewSessions,
        PermissionCatalog.Users.RevokeSessions
    };

    [Theory]
    [MemberData(nameof(DossierRights))]
    public void DossierRights_AreConfigurableButRequireExplicitNonAdministratorGrants(string key)
    {
        var definition = Assert.Single(PermissionCatalog.All, p => p.Key == key);
        Assert.False(definition.AdminOnly);
        Assert.Contains(key, PermissionCatalog.DefaultGrants(StaffRoles.Admin));
        foreach (var role in StaffRoles.All.Where(r => r != StaffRoles.Admin).Append("User").Append("Unknown"))
            Assert.DoesNotContain(key, PermissionCatalog.DefaultGrants(role));
    }

    [Theory]
    [MemberData(nameof(DossierRights))]
    public async Task Administrator_ReceivesNewDossierRightsWithoutStoredGrantRows(string key)
    {
        using var db = new StaffAccessTestContext();
        Assert.True(await new StaffAccessService(db).HasPermissionAsync(StaffAccessTestContext.Principal(), key));
    }

    [Theory]
    [MemberData(nameof(DossierRights))]
    public async Task ExistingModeratorDefaults_DoNotGainDossierRights(string key)
    {
        using var db = new StaffAccessTestContext(StaffRoles.Moderator, PermissionCatalog.DefaultGrants(StaffRoles.Moderator).ToArray());
        Assert.False(await new StaffAccessService(db).HasPermissionAsync(StaffAccessTestContext.Principal(), key));
    }

    [Theory]
    [MemberData(nameof(DossierRights))]
    public async Task ExplicitGrant_IsAvailableAndRevocationIsObservedOnNextRequest(string key)
    {
        using var db = new StaffAccessTestContext(StaffRoles.OrderManager,
            [PermissionCatalog.Users.View, PermissionCatalog.Users.ViewDossier, PermissionCatalog.Partners.View, key]);
        var principal = StaffAccessTestContext.Principal();
        Assert.True(await new StaffAccessService(db).HasPermissionAsync(principal, key));
        db.Grants.RemoveAll(p => p.PermissionKey == key);
        Assert.False(await new StaffAccessService(db).HasPermissionAsync(principal, key));
    }

    [Theory]
    [MemberData(nameof(DossierRights))]
    public async Task DossierRightsWithoutUserListView_AreRejectedBeforeDatabaseAccess(string key)
    {
        using var db = new IdentityDbContext(new DbContextOptions<IdentityDbContext>());
        var result = await new AccessManagementService(db).SaveRolePermissionsAsync(
            new ClaimsPrincipal(), StaffRoles.OrderManager, [key], 0);
        Assert.False(result.Succeeded);
        Assert.False(result.Changed);
        Assert.NotEmpty(result.Error!);
    }

    [Theory]
    [InlineData(PermissionCatalog.Users.AddNotes)]
    [InlineData(PermissionCatalog.Users.ManagePartnerLinks)]
    [InlineData(PermissionCatalog.Users.ViewDossierHistory)]
    [InlineData(PermissionCatalog.Users.EditProfile)]
    [InlineData(PermissionCatalog.Users.ManageAddresses)]
    [InlineData(PermissionCatalog.Users.Unlock)]
    [InlineData(PermissionCatalog.Users.SuspendAccess)]
    [InlineData(PermissionCatalog.Users.ViewSessions)]
    [InlineData(PermissionCatalog.Users.RevokeSessions)]
    public async Task DossierActionsWithoutDossierView_AreRejectedBeforeDatabaseAccess(string key)
    {
        using var db = new IdentityDbContext(new DbContextOptions<IdentityDbContext>());
        var result = await new AccessManagementService(db).SaveRolePermissionsAsync(
            new ClaimsPrincipal(), StaffRoles.Moderator, [PermissionCatalog.Users.View, PermissionCatalog.Partners.View, key], 0);
        Assert.False(result.Succeeded);
        Assert.False(result.Changed);
        Assert.NotEmpty(result.Error!);
    }

    [Fact]
    public async Task SessionRevocationWithoutSessionView_IsRejectedBeforeDatabaseAccess()
    {
        using var db = new IdentityDbContext(new DbContextOptions<IdentityDbContext>());
        var result = await new AccessManagementService(db).SaveRolePermissionsAsync(new ClaimsPrincipal(), StaffRoles.Moderator,
            [PermissionCatalog.Users.View, PermissionCatalog.Users.ViewDossier, PermissionCatalog.Users.RevokeSessions], 0);
        Assert.False(result.Succeeded);
        Assert.False(result.Changed);
        Assert.NotEmpty(result.Error!);
    }

    [Fact]
    public async Task PartnerLinkManagementWithoutPartnerView_IsRejectedBeforeDatabaseAccess()
    {
        using var db = new IdentityDbContext(new DbContextOptions<IdentityDbContext>());
        var result = await new AccessManagementService(db).SaveRolePermissionsAsync(new ClaimsPrincipal(), StaffRoles.Moderator,
            [PermissionCatalog.Users.View, PermissionCatalog.Users.ViewDossier, PermissionCatalog.Users.ManagePartnerLinks], 0);
        Assert.False(result.Succeeded);
        Assert.False(result.Changed);
        Assert.NotEmpty(result.Error!);
    }

    [Theory]
    [InlineData(nameof(AdminCustomersController.Dossier), PermissionCatalog.Users.View, PermissionCatalog.Users.ViewDossier)]
    [InlineData(nameof(AdminCustomersController.AddNote), PermissionCatalog.Users.View, PermissionCatalog.Users.ViewDossier, PermissionCatalog.Users.AddNotes)]
    [InlineData(nameof(AdminCustomersController.Link), PermissionCatalog.Users.View, PermissionCatalog.Users.ViewDossier, PermissionCatalog.Users.ManagePartnerLinks, PermissionCatalog.Partners.View)]
    [InlineData(nameof(AdminCustomersController.Unlink), PermissionCatalog.Users.View, PermissionCatalog.Users.ViewDossier, PermissionCatalog.Users.ManagePartnerLinks, PermissionCatalog.Partners.View)]
    public void DossierEndpointRequiresEverySeparatePermission(string actionName, params string[] requiredPermissions)
    {
        var action = typeof(AdminCustomersController).GetMethod(actionName)!;
        var permissions = action.GetCustomAttributes<RequirePermissionAttribute>().SelectMany(a => a.Permissions).ToHashSet();
        Assert.All(requiredPermissions, key => Assert.Contains(key, permissions));
    }
}
