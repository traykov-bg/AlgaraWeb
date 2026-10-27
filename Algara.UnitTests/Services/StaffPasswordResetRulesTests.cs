using System.Security.Claims;
using Algara.Data.Data;
using Algara.Identity.Authorization;
using Algara.Identity.Data;
using Algara.Identity.Services;
using Algara.UnitTests.Controllers;
using Algara.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace Algara.UnitTests.Services;

public sealed class StaffPasswordResetRulesTests
{
    [Fact]
    public async Task MixedRoles_CannotBorrowPasswordResetFromAnIneligibleRole()
    {
        using var db = new StaffAccessTestContext(StaffRoles.Moderator);
        var moderator = db.Assignments.First(r => r.UserN == db.PrimaryUser.N).Role;
        var orders = new ApplicationRole { N = 20, Name = StaffRoles.OrderManager };
        db.Assignments.Add(new UserRole { UserN = db.PrimaryUser.N, User = db.PrimaryUser, RoleN = orders.N, Role = orders });
        db.Grants.Add(new RolePermission { RoleN = orders.N, Role = orders, PermissionKey = PermissionCatalog.Users.ResetPassword });
        db.Grants.Add(new RolePermission { RoleN = orders.N, Role = orders, PermissionKey = PermissionCatalog.Orders.View });
        var principal = StaffAccessTestContext.Principal();
        var withoutEligibleGrant = new StaffAccessService(db);
        Assert.False(await withoutEligibleGrant.HasPermissionAsync(principal, PermissionCatalog.Users.ResetPassword));
        Assert.True(await withoutEligibleGrant.HasPermissionAsync(principal, PermissionCatalog.Orders.View));
        var eligible = new RolePermission { RoleN = moderator.N, Role = moderator, PermissionKey = PermissionCatalog.Users.ResetPassword };
        db.Grants.Add(eligible);
        Assert.True(await new StaffAccessService(db).HasPermissionAsync(principal, PermissionCatalog.Users.ResetPassword));
        db.Grants.Remove(eligible);
        Assert.False(await new StaffAccessService(db).HasPermissionAsync(principal, PermissionCatalog.Users.ResetPassword));
    }

    [Theory]
    [InlineData("Admin", "Admin", true)]
    [InlineData("Admin|Unrecognized", "Unrecognized", true)]
    [InlineData("Moderator", "User", true)]
    [InlineData("Moderator|User", "CatalogEditor|User", true)]
    [InlineData("Moderator", "OrderManager|CatalogEditor", true)]
    [InlineData("Moderator", "Moderator", false)]
    [InlineData("Moderator", "Admin|CatalogEditor", false)]
    [InlineData("Moderator", "Unrecognized", false)]
    [InlineData("Moderator|Unrecognized", "User", false)]
    [InlineData("OrderManager", "User", false)]
    [InlineData("CatalogEditor", "User", false)]
    [InlineData("User", "User", false)]
    [InlineData("", "User", false)]
    public void PasswordResetRoleHierarchy_AllowsOnlyAuthorizedActorAndTargetCombinations(string actor, string target, bool allowed)
    {
        Assert.Equal(allowed, StaffAccountManagementRules.CanResetPassword(Roles(actor), Roles(target)));
    }

    [Theory]
    [InlineData("OrderManager", "User", true)]
    [InlineData("CatalogEditor|User", "User", true)]
    [InlineData("OrderManager", "CatalogEditor", false)]
    [InlineData("OrderManager|Unrecognized", "User", false)]
    [InlineData("Moderator", "CatalogEditor", true)]
    [InlineData("Moderator", "Moderator|User", false)]
    [InlineData("Admin", "Unrecognized", true)]
    [InlineData("User", "", false)]
    public void SharedHierarchy_PreservesOrdinaryCustomerSupportAndFailsClosed(string actor, string target, bool allowed)
    {
        Assert.Equal(allowed, StaffAccountManagementRules.CanManage(Roles(actor), Roles(target)));
    }

    [Theory]
    [InlineData(6)]
    [InlineData(1024)]
    public void PasswordCodec_VerifiesExistingFormatWithoutNormalization(int length)
    {
        var password = new string('x', length - 1) + " ";
        var credential = AccountPasswordCodec.Create(password);
        Assert.True(AccountPasswordCodec.Verify(password, credential.Hash, credential.Salt));
        Assert.False(AccountPasswordCodec.Verify(password.TrimEnd(), credential.Hash, credential.Salt));
        Assert.False(AccountPasswordCodec.Verify(new string('y', length), credential.Hash, credential.Salt));
    }

    [Fact]
    public void PasswordCodec_RejectsMalformedCredentialsAndUnboundedPassword()
    {
        var credential = AccountPasswordCodec.Create("synthetic-unit-secret");
        Assert.False(AccountPasswordCodec.Verify(null, credential.Hash, credential.Salt));
        Assert.False(AccountPasswordCodec.Verify("", credential.Hash, credential.Salt));
        Assert.False(AccountPasswordCodec.Verify(new string('x', 1025), credential.Hash, credential.Salt));
        Assert.False(AccountPasswordCodec.Verify("synthetic-unit-secret", "invalid", credential.Salt));
        Assert.False(AccountPasswordCodec.Verify("synthetic-unit-secret", credential.Hash, "invalid"));
        Assert.False(AccountPasswordCodec.Verify("synthetic-unit-secret", Convert.ToBase64String(new byte[31]), credential.Salt));
        Assert.False(AccountPasswordCodec.Verify("synthetic-unit-secret", credential.Hash, Convert.ToBase64String(new byte[15])));
    }

    [Theory]
    [InlineData("short", "short", "actor-secret")]
    [InlineData("valid-new-secret", "different-secret", "actor-secret")]
    [InlineData("valid-new-secret", "valid-new-secret", null)]
    public async Task InvalidCredentialRequests_AreRejectedBeforeDatabaseAccessAndClearSecrets(string newPassword, string confirmation, string? actorPassword)
    {
        using var shop = new ShopDbContext(new DbContextOptions<ShopDbContext>());
        var request = new CustomerPasswordReset
        {
            NewPassword = newPassword, ConfirmPassword = confirmation, ActorPassword = actorPassword,
            Reason = "Synthetic support case"
        };
        var result = await new CustomerManagementService(shop).ResetPasswordAsync(new ClaimsPrincipal(), 2, request);
        Assert.False(result.Success);
        Assert.Null(request.ActorPassword);
        Assert.Null(request.NewPassword);
        Assert.Null(request.ConfirmPassword);
        Assert.DoesNotContain(newPassword, result.Error ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AuditReasonCannotContainSuppliedSecret()
    {
        using var shop = new ShopDbContext(new DbContextOptions<ShopDbContext>());
        var request = new CustomerPasswordReset
        {
            ActorPassword = "synthetic-actor-secret", NewPassword = "synthetic-new-secret", ConfirmPassword = "synthetic-new-secret",
            Reason = "Accidentally pasted synthetic-new-secret"
        };
        var result = await new CustomerManagementService(shop).ResetPasswordAsync(new ClaimsPrincipal(), 2, request);
        Assert.False(result.Success);
        Assert.DoesNotContain("synthetic-new-secret", result.Error ?? string.Empty, StringComparison.Ordinal);
        Assert.Null(request.ActorPassword); Assert.Null(request.NewPassword); Assert.Null(request.ConfirmPassword);
    }

    [Fact]
    public async Task MissingAccountVersionRejectsBeforeDatabaseAccessAndClearsSecrets()
    {
        using var shop = new ShopDbContext(new DbContextOptions<ShopDbContext>());
        var request = new CustomerPasswordReset
        {
            ActorPassword = "synthetic-actor-secret", NewPassword = "synthetic-new-secret", ConfirmPassword = "synthetic-new-secret",
            Reason = "Synthetic support case"
        };
        var result = await new CustomerManagementService(shop).ResetPasswordAsync(new ClaimsPrincipal(), 2, request);
        Assert.False(result.Success);
        Assert.Null(request.ActorPassword); Assert.Null(request.NewPassword); Assert.Null(request.ConfirmPassword);
    }

    private static string[] Roles(string value) => value.Split('|', StringSplitOptions.RemoveEmptyEntries);
}
