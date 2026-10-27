using Algara.Data.Models;
using Algara.Identity.Authorization;
using Algara.Identity.Models;
using Algara.Web.Services;

namespace Algara.UnitTests.Services;

public sealed class CustomerAssistanceRulesTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(-1, false)]
    public void OnlyExplicitPreviewAndAssistModesAreAllowed(int mode, bool allowed) =>
        Assert.Equal(allowed, CustomerAssistanceRules.IsMode((CustomerAssistanceMode)mode));

    [Theory]
    [InlineData("Admin", "User", true)]
    [InlineData("Admin", "Admin", true)]
    [InlineData("Admin", "Moderator", true)]
    [InlineData("Admin", "CatalogEditor", true)]
    [InlineData("Admin", "OrderManager", true)]
    [InlineData("Admin", "UnknownPrivilege", true)]
    [InlineData("Moderator", "User", true)]
    [InlineData("Moderator", "CatalogEditor", true)]
    [InlineData("Moderator", "OrderManager", true)]
    [InlineData("Moderator", "Admin", false)]
    [InlineData("Moderator", "Moderator", false)]
    [InlineData("Moderator", "UnknownPrivilege", false)]
    [InlineData("Moderator", "user", false)]
    [InlineData("Moderator", "", false)]
    [InlineData("CatalogEditor", "User", true)]
    [InlineData("CatalogEditor", "CatalogEditor", false)]
    [InlineData("CatalogEditor", "OrderManager", false)]
    [InlineData("CatalogEditor", "Moderator", false)]
    [InlineData("OrderManager", "User", true)]
    [InlineData("OrderManager", "Admin", false)]
    [InlineData("OrderManager", "CatalogEditor", false)]
    [InlineData("User", "User", false)]
    [InlineData("UnknownPrivilege", "User", false)]
    public void AssistanceTargetEligibilityUsesTheStaffHierarchy(string actorRole, string targetRole, bool allowed)
    {
        Assert.Equal(allowed, CustomerAssistanceRules.CanTarget(new ApplicationUser { N = 10, IsActive = true }, [targetRole], 20, [actorRole]));
    }

    [Fact]
    public void ACustomerRoleDoesNotMaskAnAdditionalStaffRole()
    {
        Assert.False(CustomerAssistanceRules.CanTarget(new ApplicationUser { N = 10, IsActive = true }, ["User", "Admin"], 20, ["Moderator"]));
        Assert.False(CustomerAssistanceRules.CanTarget(new ApplicationUser { N = 10, IsActive = true }, ["User", "Moderator"], 20, ["Moderator"]));
    }

    [Fact]
    public void ModeratorCanAssistAnAccountWithBothLowerStaffRoles()
    {
        Assert.True(CustomerAssistanceRules.CanTarget(new ApplicationUser { N = 10, IsActive = true },
            ["User", "CatalogEditor", "OrderManager"], 20, ["User", "Moderator"]));
    }

    [Fact]
    public void AdministratorMembershipTakesPrecedenceForAnotherPrivilegedTarget()
    {
        Assert.True(CustomerAssistanceRules.CanTarget(new ApplicationUser { N = 10, IsActive = true },
            ["User", "Admin", "Moderator"], 20, ["User", "Moderator", "Admin"]));
    }

    [Fact]
    public void LosingAdministratorHierarchyRejectsAProtectedTargetWithTheSameTargetData()
    {
        var user = new ApplicationUser { N = 10, IsActive = true };
        Assert.True(CustomerAssistanceRules.CanTarget(user, ["Moderator"], 20, ["Admin"]));
        Assert.False(CustomerAssistanceRules.CanTarget(user, ["Moderator"], 20, ["Moderator"]));
    }

    [Fact]
    public void SelfInactiveAndSuspendedAccountsAreNotTargets()
    {
        var user = new ApplicationUser { N = 10, IsActive = true };
        Assert.False(CustomerAssistanceRules.CanTarget(user, ["Admin"], 10, ["Admin"]));
        user.IsActive = false;
        Assert.False(CustomerAssistanceRules.CanTarget(user, ["User"], 20, ["Admin"]));
        user.IsActive = true;
        user.AccessSuspendedAtUtc = DateTime.UnixEpoch;
        Assert.False(CustomerAssistanceRules.CanTarget(user, ["User"], 20, ["Admin"]));
    }

    [Fact]
    public void LoginLockoutAndPendingEmailDoNotRequireAClientLoginToReceiveAssistance()
    {
        var user = new ApplicationUser
        {
            N = 10, IsActive = true, FailedLoginAttempts = 5, LockoutUntil = DateTime.MaxValue,
            RequireEmailConfirmation = true, EmailConfirmed = false, LastLoginSessionId = null
        };
        Assert.True(CustomerAssistanceRules.CanTarget(user, ["User"], 20, ["Moderator"]));
    }

    [Fact]
    public void SessionLifetimeIncludesItsStartAndExcludesItsExactEnd()
    {
        var session = Session();
        Assert.Equal(TimeSpan.FromMinutes(30), CustomerAssistanceRules.Duration);
        Assert.False(CustomerAssistanceRules.IsActive(session, session.StartedAtUtc.AddTicks(-1)));
        Assert.True(CustomerAssistanceRules.IsActive(session, session.StartedAtUtc));
        Assert.True(CustomerAssistanceRules.IsActive(session, session.ExpiresAtUtc.AddTicks(-1)));
        Assert.False(CustomerAssistanceRules.IsActive(session, session.ExpiresAtUtc));
        Assert.False(CustomerAssistanceRules.IsActive(session, session.ExpiresAtUtc.AddTicks(1)));
    }

    [Theory]
    [InlineData(15)]
    [InlineData(29)]
    [InlineData(31)]
    [InlineData(60)]
    public void PersistedSessionCannotSilentlyUseAnotherLifetime(int minutes)
    {
        var session = Session();
        session.ExpiresAtUtc = session.StartedAtUtc.AddMinutes(minutes);
        Assert.False(CustomerAssistanceRules.IsActive(session, session.StartedAtUtc.AddMinutes(1)));
    }

    [Fact]
    public void EndedOrUnknownModeSessionIsNeverActive()
    {
        var session = Session();
        session.EndedAtUtc = session.StartedAtUtc.AddMinutes(1);
        Assert.False(CustomerAssistanceRules.IsActive(session, session.StartedAtUtc.AddMinutes(2)));
        session.EndedAtUtc = null;
        session.Mode = 99;
        Assert.False(CustomerAssistanceRules.IsActive(session, session.StartedAtUtc.AddMinutes(2)));
    }

    [Theory]
    [InlineData("stamp")]
    [InlineData("email")]
    [InlineData("username")]
    [InlineData("active")]
    [InlineData("suspended")]
    [InlineData("required-confirmation")]
    [InlineData("confirmed")]
    [InlineData("id")]
    [InlineData("numeric-id")]
    public void SecurityVersionChangesForAccessAndIdentityChanges(string field)
    {
        var user = new ApplicationUser { N = 10, Id = "customer-id", SecurityStamp = "original" };
        var version = CustomerAssistanceRules.SecurityVersion(user, ["User"]);
        switch (field)
        {
            case "stamp": user.SecurityStamp = "updated"; break;
            case "email": user.Email = "updated@example.test"; break;
            case "username": user.UserName = "updated@example.test"; break;
            case "active": user.IsActive = !user.IsActive; break;
            case "suspended": user.AccessSuspendedAtUtc = DateTime.UnixEpoch; break;
            case "required-confirmation": user.RequireEmailConfirmation = !user.RequireEmailConfirmation; break;
            case "confirmed": user.EmailConfirmed = !user.EmailConfirmed; break;
            case "id": user.Id = "different-id"; break;
            case "numeric-id": user.N++; break;
        }
        Assert.NotEqual(version, CustomerAssistanceRules.SecurityVersion(user, ["User"]));
    }

    [Fact]
    public void ContactEditsDoNotInvalidateTheAssistanceContextOrExposeSecretMaterial()
    {
        var user = new ApplicationUser { N = 10, SecurityStamp = "private-stamp", Email = "customer@example.test" };
        var version = CustomerAssistanceRules.SecurityVersion(user, ["User"]);
        user.FirstName = "Changed"; user.LastName = "Customer"; user.FullName = "Changed Customer";
        user.DisplayName = "Changed Customer"; user.PhoneNumber = "+359888123456";
        Assert.Equal(version, CustomerAssistanceRules.SecurityVersion(user, ["User"]));
        Assert.Equal(32, Convert.FromBase64String(version).Length);
        Assert.DoesNotContain("private-stamp", version);
        Assert.DoesNotContain("customer@example.test", version);
    }

    [Fact]
    public void LegacyEmptyStampIsDistinctFromMissingStamp()
    {
        var user = new ApplicationUser { N = 10, SecurityStamp = string.Empty };
        var version = CustomerAssistanceRules.SecurityVersion(user, ["User"]);
        user.SecurityStamp = null!;
        Assert.NotEqual(version, CustomerAssistanceRules.SecurityVersion(user, ["User"]));
    }

    [Fact]
    public void TargetRoleChangesInvalidateAnExistingContextEvenIfTheNewRoleWouldBeAllowed()
    {
        var user = new ApplicationUser { N = 10, IsActive = true };
        Assert.True(CustomerAssistanceRules.CanTarget(user, ["CatalogEditor"], 20, ["Moderator"]));
        Assert.True(CustomerAssistanceRules.CanTarget(user, ["OrderManager"], 20, ["Moderator"]));
        Assert.NotEqual(CustomerAssistanceRules.SecurityVersion(user, ["CatalogEditor"]),
            CustomerAssistanceRules.SecurityVersion(user, ["OrderManager"]));
        Assert.NotEqual(CustomerAssistanceRules.SecurityVersion(user, ["User"]),
            CustomerAssistanceRules.SecurityVersion(user, ["User", "Admin"]));
    }

    [Fact]
    public void RoleOrderingAndDuplicateMembershipAreNotSecurityChanges()
    {
        var user = new ApplicationUser { N = 10, IsActive = true };
        Assert.Equal(CustomerAssistanceRules.SecurityVersion(user, ["User", "CatalogEditor", "OrderManager"]),
            CustomerAssistanceRules.SecurityVersion(user, ["OrderManager", "User", "CatalogEditor", "User"]));
    }

    [Theory]
    [InlineData(PermissionCatalog.Users.PreviewCustomer)]
    [InlineData(PermissionCatalog.Users.AssistCustomer)]
    public void AssistanceRightsRequireDossierAndAreExplicitForEveryNonAdminRole(string permission)
    {
        Assert.NotNull(PermissionCatalog.Find(permission));
        Assert.True(PermissionCatalog.RequiresCustomerDossier(permission));
        Assert.Contains(permission, PermissionCatalog.DefaultGrants(StaffRoles.Admin));
        foreach (var role in StaffRoles.All.Where(r => r != StaffRoles.Admin))
            Assert.DoesNotContain(permission, PermissionCatalog.DefaultGrants(role));
    }

    private static CustomerAssistanceSession Session() => new()
    {
        Mode = (int)CustomerAssistanceMode.Assist,
        StartedAtUtc = DateTime.UnixEpoch,
        ExpiresAtUtc = DateTime.UnixEpoch.AddMinutes(30)
    };
}
