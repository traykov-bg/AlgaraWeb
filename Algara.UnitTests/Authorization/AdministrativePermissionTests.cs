using System.Reflection;
using Algara.Identity.Authorization;
using Algara.Web.Authorization;
using Algara.Web.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Options;

namespace Algara.UnitTests.Authorization;

public class AdministrativePermissionTests
{
    [Fact]
    public void EveryAdministrativeEndpointRequiresStaffAndKnownActionPermissions()
    {
        var controllers = typeof(AdminController).Assembly.GetTypes()
            .Where(t => t.Name.StartsWith("Admin", StringComparison.Ordinal) &&
                typeof(Controller).IsAssignableFrom(t)).ToArray();
        Assert.NotEmpty(controllers);
        foreach (var controller in controllers)
        {
            var controllerAttributes = controller.GetCustomAttributes(true);
            Assert.Contains(controllerAttributes.OfType<IAuthorizeData>(),
                a => a.Policy == RequirePermissionAttribute.StaffPolicy);
            foreach (var action in controller.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                         .Where(m => m.GetCustomAttributes<HttpMethodAttribute>(true).Any()))
            {
                var attributes = controllerAttributes.Concat(action.GetCustomAttributes(true)).ToArray();
                Assert.DoesNotContain(attributes, a => a is IAllowAnonymous);
                Assert.DoesNotContain(attributes.OfType<IAuthorizeData>(), a => !string.IsNullOrEmpty(a.Roles));
                var required = attributes.OfType<RequirePermissionAttribute>().ToArray();
                Assert.True(required.Length > 0, $"Missing permission on {controller.Name}.{action.Name}.");
                foreach (var permission in required)
                {
                    Assert.NotEmpty(permission.Permissions);
                    Assert.All(permission.Permissions, key => Assert.NotNull(PermissionCatalog.Find(key)));
                }
                if (action.GetCustomAttributes<HttpMethodAttribute>(true).Any(a =>
                        a.HttpMethods.Any(m => m != "GET" && m != "HEAD")))
                    Assert.Contains(attributes, a => a is ValidateAntiForgeryTokenAttribute or AutoValidateAntiforgeryTokenAttribute);
            }
        }
    }

    [Theory]
    [InlineData(nameof(AdminController.AssignRole))]
    [InlineData(nameof(AdminController.RemoveRole))]
    public void RoleMutationsRequireTheAdminOnlyAssignmentPermission(string actionName)
    {
        var action = typeof(AdminController).GetMethod(actionName)!;
        Assert.Contains(action.GetCustomAttributes<RequirePermissionAttribute>(),
            a => a.Permissions.Contains(PermissionCatalog.Access.AssignRoles));
        Assert.True(PermissionCatalog.Find(PermissionCatalog.Access.AssignRoles)!.AdminOnly);
    }

    [Fact]
    public async Task MultiPermissionPolicyRetainsEveryRequiredActionAndAuthentication()
    {
        var provider = new StaffPolicyProvider(Options.Create(new AuthorizationOptions()));
        var policy = await provider.GetPolicyAsync("Permission:Products.View,Products.Edit,Products.ChangePrice");
        Assert.NotNull(policy);
        Assert.Contains(policy.Requirements, r => r is DenyAnonymousAuthorizationRequirement);
        var requirement = Assert.Single(policy.Requirements.OfType<PermissionRequirement>());
        Assert.Equal(new[] { "Products.View", "Products.Edit", "Products.ChangePrice" }, requirement.Permissions);
    }

    [Fact]
    public async Task StaffEntryPolicyRequiresAuthenticationAndCurrentStaffCheck()
    {
        var provider = new StaffPolicyProvider(Options.Create(new AuthorizationOptions()));
        var policy = await provider.GetPolicyAsync(RequirePermissionAttribute.StaffPolicy);
        Assert.NotNull(policy);
        Assert.Contains(policy.Requirements, r => r is DenyAnonymousAuthorizationRequirement);
        Assert.Single(policy.Requirements.OfType<StaffRequirement>());
    }

    [Fact]
    public async Task UnrelatedPoliciesStillUseStandardAuthorizationConfiguration()
    {
        var options = new AuthorizationOptions();
        options.AddPolicy("ExistingPolicy", p => p.RequireClaim("test-claim"));
        var provider = new StaffPolicyProvider(Options.Create(options));
        Assert.Same(options.GetPolicy("ExistingPolicy"), await provider.GetPolicyAsync("ExistingPolicy"));
        Assert.Null(await provider.GetPolicyAsync("UndefinedPolicy"));
    }

    [Fact]
    public void ConventionRejectsNewStaffActionWithoutExplicitPermission()
    {
        var application = ApplicationFor([]);
        Assert.Throws<InvalidOperationException>(() => new StaffPermissionConvention().Apply(application));
    }

    [Theory]
    [InlineData("Unknown.Permission")]
    [InlineData("")]
    public void ConventionRejectsUnknownAndEmptyPermissionMappings(string key)
    {
        var application = ApplicationFor([new RequirePermissionAttribute(key)]);
        Assert.Throws<InvalidOperationException>(() => new StaffPermissionConvention().Apply(application));
    }

    [Fact]
    public void ConventionRejectsAnonymousOverrideEvenWithValidPermission()
    {
        var application = ApplicationFor([new RequirePermissionAttribute("Products.View"), new AllowAnonymousAttribute()]);
        Assert.Throws<InvalidOperationException>(() => new StaffPermissionConvention().Apply(application));
    }

    [Fact]
    public void ConventionAcceptsKnownStaffPermissionAndLeavesCustomerControllersAlone()
    {
        var application = ApplicationFor([new RequirePermissionAttribute("Products.View", "Products.Edit")]);
        var customer = new ControllerModel(typeof(TestController).GetTypeInfo(), [new AuthorizeAttribute()]);
        customer.Actions.Add(new ActionModel(typeof(TestController).GetMethod(nameof(TestController.Action))!, []));
        application.Controllers.Add(customer);
        new StaffPermissionConvention().Apply(application);
    }

    private static ApplicationModel ApplicationFor(IReadOnlyList<object> attributes)
    {
        var application = new ApplicationModel();
        var controller = new ControllerModel(typeof(TestController).GetTypeInfo(),
            [new AuthorizeAttribute { Policy = RequirePermissionAttribute.StaffPolicy }]);
        controller.Actions.Add(new ActionModel(typeof(TestController).GetMethod(nameof(TestController.Action))!, attributes));
        application.Controllers.Add(controller);
        return application;
    }

    private sealed class TestController : Controller
    {
        public IActionResult Action() => Ok();
    }
}
