using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Security.Claims;
using Algara.Identity.Authorization;
using Algara.Identity.Data;
using Algara.Web.Authorization;
using Algara.Web.Controllers;
using Algara.Web.ViewModels;
using Algara.Web.ViewModels.Admin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;

namespace Algara.UnitTests.Identity;

public sealed class CustomerManagementEndpointTests
{
    [Theory]
    [InlineData(nameof(AdminCustomersController.EditProfile), PermissionCatalog.Users.EditProfile)]
    [InlineData(nameof(AdminCustomersController.SaveAddress), PermissionCatalog.Users.ManageAddresses)]
    [InlineData(nameof(AdminCustomersController.DeleteAddress), PermissionCatalog.Users.ManageAddresses)]
    [InlineData(nameof(AdminCustomersController.SetDefaultAddress), PermissionCatalog.Users.ManageAddresses)]
    [InlineData(nameof(AdminCustomersController.Unlock), PermissionCatalog.Users.Unlock)]
    [InlineData(nameof(AdminCustomersController.SuspendAccess), PermissionCatalog.Users.SuspendAccess)]
    [InlineData(nameof(AdminCustomersController.RestoreAccess), PermissionCatalog.Users.SuspendAccess)]
    [InlineData(nameof(AdminCustomersController.RevokeSession), PermissionCatalog.Users.RevokeSessions)]
    [InlineData(nameof(AdminCustomersController.RevokeAllSessions), PermissionCatalog.Users.RevokeSessions)]
    public void Mutations_RequirePostAntiforgeryDossierAndSpecificPermission(string method, string permission)
    {
        var endpoint = typeof(AdminCustomersController).GetMethod(method)!;
        Assert.NotNull(endpoint.GetCustomAttribute<HttpPostAttribute>());
        Assert.NotNull(endpoint.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
        var permissions = endpoint.GetCustomAttributes<RequirePermissionAttribute>().SelectMany(a => a.Permissions).ToHashSet();
        Assert.Contains(PermissionCatalog.Users.View, permissions);
        Assert.Contains(PermissionCatalog.Users.ViewDossier, permissions);
        Assert.Contains(permission, permissions);
        if (permission == PermissionCatalog.Users.RevokeSessions) Assert.Contains(PermissionCatalog.Users.ViewSessions, permissions);
    }

    [Fact]
    public async Task RevokeGrantWithoutSessionView_IsRejectedBeforeDatabaseUse()
    {
        using var db = new IdentityDbContext(new DbContextOptions<IdentityDbContext>());
        var result = await new AccessManagementService(db).SaveRolePermissionsAsync(new ClaimsPrincipal(), StaffRoles.OrderManager,
            [PermissionCatalog.Users.View, PermissionCatalog.Users.ViewDossier, PermissionCatalog.Users.RevokeSessions], 0);
        Assert.False(result.Succeeded); Assert.False(result.Changed); Assert.NotEmpty(result.Error!);
    }

    [Theory]
    [InlineData(typeof(CustomerProfileEditForm), nameof(CustomerProfileEditForm.ExpectedAccountVersion))]
    [InlineData(typeof(CustomerAddressEditForm), nameof(CustomerAddressEditForm.ExpectedAccountVersion))]
    [InlineData(typeof(CustomerAddressEditForm), nameof(CustomerAddressEditForm.ExpectedAddressesVersion))]
    [InlineData(typeof(CustomerAddressActionForm), nameof(CustomerAddressActionForm.ExpectedAccountVersion))]
    [InlineData(typeof(CustomerAddressActionForm), nameof(CustomerAddressActionForm.ExpectedAddressesVersion))]
    [InlineData(typeof(CustomerSessionActionForm), nameof(CustomerSessionActionForm.ExpectedAccountVersion))]
    [InlineData(typeof(CustomerSessionActionForm), nameof(CustomerSessionActionForm.ExpectedSessionVersion))]
    [InlineData(typeof(ProfileDetailsViewModel), nameof(ProfileDetailsViewModel.ExpectedVersion))]
    [InlineData(typeof(AddressFormViewModel), nameof(AddressFormViewModel.ExpectedVersion))]
    [InlineData(typeof(AddressFormViewModel), nameof(AddressFormViewModel.ExpectedAccountVersion))]
    public void ConcurrencyVersions_RejectMissingOrEmptyFormValues(Type formType, string name)
    {
        var property = formType.GetProperty(name)!;
        Assert.NotNull(property.GetCustomAttribute<BindRequiredAttribute>());
        var required = Assert.IsType<RequiredAttribute>(property.GetCustomAttribute<RequiredAttribute>());
        Assert.False(required.IsValid(null)); Assert.False(required.IsValid(""));
    }
}
