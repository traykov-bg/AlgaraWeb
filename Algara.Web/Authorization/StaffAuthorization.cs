using Algara.Identity.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.Extensions.Options;

namespace Algara.Web.Authorization;

public sealed record StaffRequirement : IAuthorizationRequirement;
public sealed record PermissionRequirement(string[] Permissions) : IAuthorizationRequirement;

public sealed class StaffPolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    public override Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName == RequirePermissionAttribute.StaffPolicy)
            return Task.FromResult<AuthorizationPolicy?>(new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser().AddRequirements(new StaffRequirement()).Build());
        if (policyName.StartsWith(RequirePermissionAttribute.Prefix, StringComparison.Ordinal))
            return Task.FromResult<AuthorizationPolicy?>(new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(policyName[RequirePermissionAttribute.Prefix.Length..].Split(',')))
                .Build());
        return base.GetPolicyAsync(policyName);
    }
}

public sealed class StaffAuthorizationHandler(StaffAccessService access, IHttpContextAccessor accessor)
    : IAuthorizationHandler
{
    public async Task HandleAsync(AuthorizationHandlerContext context)
    {
        var cancellationToken = accessor.HttpContext?.RequestAborted ?? CancellationToken.None;
        foreach (var requirement in context.PendingRequirements.ToArray())
        {
            if (requirement is StaffRequirement && await access.IsStaffAsync(context.User, cancellationToken))
                context.Succeed(requirement);
            if (requirement is PermissionRequirement permission && permission.Permissions.Length > 0)
            {
                var allowed = true;
                foreach (var key in permission.Permissions)
                    if (!await access.HasPermissionAsync(context.User, key, cancellationToken))
                    {
                        allowed = false;
                        break;
                    }
                if (allowed) context.Succeed(requirement);
            }
        }
    }
}

/// <summary>New administrative actions fail closed until their permission is explicitly mapped.</summary>
public sealed class StaffPermissionConvention : IApplicationModelConvention
{
    public void Apply(ApplicationModel application)
    {
        foreach (var controller in application.Controllers)
        {
            var isStaff = controller.Attributes.OfType<IAuthorizeData>()
                .Any(a => a.Policy == RequirePermissionAttribute.StaffPolicy);
            if (!isStaff) continue;
            foreach (var action in controller.Actions)
            {
                var permissions = controller.Attributes.Concat(action.Attributes)
                    .OfType<RequirePermissionAttribute>().ToArray();
                if (permissions.Length == 0 || permissions.Any(p => p.Permissions.Length == 0 ||
                    p.Permissions.Any(k => !PermissionCatalog.All.Any(d => d.Key == k))))
                    throw new InvalidOperationException($"Administrative action {controller.ControllerName}.{action.ActionName} has no valid permission mapping.");
                if (controller.Attributes.Concat(action.Attributes).OfType<IAllowAnonymous>().Any())
                    throw new InvalidOperationException($"Administrative action {controller.ControllerName}.{action.ActionName} cannot allow anonymous access.");
            }
        }
    }
}
