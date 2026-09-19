using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace VMS.Shared.Authorization;

public sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}

public sealed class SuperAdminRequirement : IAuthorizationRequirement;

/// <summary>Builds <c>perm:CODE</c> policies on demand, and the fixed <c>SuperAdmin</c> policy.</summary>
public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName == RequireSuperAdminAttribute.PolicyName)
            return new AuthorizationPolicyBuilder().RequireAuthenticatedUser().AddRequirements(new SuperAdminRequirement()).Build();

        if (policyName.StartsWith(RequirePermissionAttribute.PolicyPrefix, StringComparison.Ordinal))
            return new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(policyName[RequirePermissionAttribute.PolicyPrefix.Length..]))
                .Build();

        return await base.GetPolicyAsync(policyName);
    }
}

/// <summary>Grants a permission when the token carries it, or the caller is a Super Admin.</summary>
public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (context.User.HasPermission(requirement.Permission))
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}

public sealed class SuperAdminAuthorizationHandler : AuthorizationHandler<SuperAdminRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, SuperAdminRequirement requirement)
    {
        if (context.User.IsSuperAdmin())
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
