using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Security.Claims;
using Algara.Identity.Authorization;
using Algara.Identity.Data;
using Algara.UnitTests.Controllers;
using Algara.Web.Authorization;
using Algara.Web.Controllers;
using Algara.Web.ViewModels.Admin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Algara.UnitTests.Identity;

public sealed class StaffPasswordResetEndpointTests
{
    [Fact]
    public void ResetRequiresPostAntiforgeryAndAllThreePermissions()
    {
        var method = typeof(AdminCustomersController).GetMethod(nameof(AdminCustomersController.ResetPassword))!;
        Assert.Equal("password", method.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Null(method.GetCustomAttribute<HttpGetAttribute>());
        Assert.NotNull(method.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
        var permissions = method.GetCustomAttributes<RequirePermissionAttribute>().SelectMany(p => p.Permissions).ToArray();
        Assert.Contains(PermissionCatalog.Users.View, permissions);
        Assert.Contains(PermissionCatalog.Users.ViewDossier, permissions);
        Assert.Contains(PermissionCatalog.Users.ResetPassword, permissions);
        Assert.True(method.GetCustomAttribute<ResponseCacheAttribute>()!.NoStore);
    }

    [Theory]
    [InlineData(StaffRoles.CatalogEditor)]
    [InlineData(StaffRoles.OrderManager)]
    public async Task MatrixCannotGrantResetToLowerStaff(string role)
    {
        using var db = new IdentityDbContext(new DbContextOptions<IdentityDbContext>());
        var result = await new AccessManagementService(db).SaveRolePermissionsAsync(new ClaimsPrincipal(), role,
            [PermissionCatalog.Users.View, PermissionCatalog.Users.ViewDossier, PermissionCatalog.Users.ResetPassword], 0);
        Assert.False(result.Succeeded);
        Assert.False(result.Changed);
    }

    [Fact]
    public async Task ModeratorResetGrantRequiresDossier()
    {
        using var db = new IdentityDbContext(new DbContextOptions<IdentityDbContext>());
        var result = await new AccessManagementService(db).SaveRolePermissionsAsync(new ClaimsPrincipal(), StaffRoles.Moderator,
            [PermissionCatalog.Users.View, PermissionCatalog.Users.ResetPassword], 0);
        Assert.False(result.Succeeded);
    }

    [Theory]
    [InlineData(StaffRoles.CatalogEditor, false)]
    [InlineData(StaffRoles.OrderManager, false)]
    [InlineData("User", false)]
    [InlineData(StaffRoles.Moderator, true)]
    [InlineData(StaffRoles.Admin, true)]
    public async Task ForgedGrantCannotBypassActorRoleCeiling(string role, bool allowed)
    {
        using var db = new StaffAccessTestContext(role, [PermissionCatalog.Users.ResetPassword]);
        var principal = StaffAccessTestContext.Principal();
        ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim(ClaimTypes.Role, StaffRoles.Admin));
        Assert.Equal(allowed, await new StaffAccessService(db).HasPermissionAsync(principal, PermissionCatalog.Users.ResetPassword));
    }

    [Fact]
    public void PasswordResetHasNoNonAdminDefaults()
    {
        Assert.True(PermissionCatalog.RequiresCustomerDossier(PermissionCatalog.Users.ResetPassword));
        Assert.Contains(PermissionCatalog.Users.ResetPassword, PermissionCatalog.DefaultGrants(StaffRoles.Admin));
        foreach (var role in StaffRoles.All.Where(r => r != StaffRoles.Admin))
            Assert.DoesNotContain(PermissionCatalog.Users.ResetPassword, PermissionCatalog.DefaultGrants(role));
    }

    [Theory]
    [InlineData(5, false)]
    [InlineData(6, true)]
    [InlineData(1024, true)]
    [InlineData(1025, false)]
    public void PasswordLengthMatchesExistingPolicy(int length, bool valid)
    {
        var form = ValidForm();
        form.NewPassword = form.ConfirmPassword = new string('x', length);
        Assert.Equal(valid, Validate(form));
    }

    [Theory]
    [InlineData("ActorPassword")]
    [InlineData("NewPassword")]
    [InlineData("ConfirmPassword")]
    [InlineData("Reason")]
    [InlineData("ExpectedAccountVersion")]
    public void MissingSecurityInputsAreRejected(string property)
    {
        var form = ValidForm();
        typeof(CustomerPasswordResetForm).GetProperty(property)!.SetValue(form, null);
        Assert.False(Validate(form));
    }

    [Fact]
    public void ConfirmationMustMatch()
    {
        var form = ValidForm();
        form.ConfirmPassword = "different";
        Assert.False(Validate(form));
    }

    [Fact]
    public void HistoryExposesOnlySafeResetMetadata()
    {
        var fields = CustomerManagementHistory.Format("PasswordResetByStaff", null,
            """{"PasswordChanged":true,"RevokedSessionCount":2,"RevokedTokenCount":1,"CancelledEmailCount":1,"PasswordHash":"secret-hash","NewPassword":"secret-password","Salt":"secret-salt","SecurityStamp":"secret-stamp"}""");
        Assert.Equal(4, fields.Count);
        Assert.DoesNotContain(fields, field => field.After.Contains("secret", StringComparison.Ordinal));
    }

    private static CustomerPasswordResetForm ValidForm() => new()
    {
        ExpectedAccountVersion = "version", Reason = "Verified support request",
        ActorPassword = "actor-fixture", NewPassword = "target-fixture", ConfirmPassword = "target-fixture"
    };

    private static bool Validate(object form) => Validator.TryValidateObject(form, new ValidationContext(form), [], true);
}
