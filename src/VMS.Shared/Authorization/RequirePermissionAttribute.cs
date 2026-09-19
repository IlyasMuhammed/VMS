using Microsoft.AspNetCore.Authorization;

namespace VMS.Shared.Authorization;

/// <summary>
/// <c>[RequirePermission(PermissionCodes.USER_MANAGE)]</c> — resolved into a policy by
/// <see cref="PermissionPolicyProvider"/> and evaluated by <see cref="PermissionAuthorizationHandler"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequirePermissionAttribute : AuthorizeAttribute
{
    public const string PolicyPrefix = "perm:";

    public RequirePermissionAttribute(string permission) => Policy = PolicyPrefix + permission;
}

/// <summary>Restricts an endpoint to platform Super Admins (the <c>is_super_admin</c> claim).</summary>
public sealed class RequireSuperAdminAttribute : AuthorizeAttribute
{
    public const string PolicyName = "SuperAdmin";

    public RequireSuperAdminAttribute() => Policy = PolicyName;
}
