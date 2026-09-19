namespace VMS.Shared.Authorization;

/// <summary>
/// Every permission a role can hold. Platform-level (cross-tenant) access is deliberately NOT a
/// permission: it is the <c>is_super_admin</c> claim, which comes from the SuperAdminUsers table
/// and cannot be granted by editing a role — otherwise a tenant admin could mint themselves a
/// platform-wide role.
/// </summary>
public static class PermissionCodes
{
    public const string USER_VIEW   = "USER_VIEW";
    public const string USER_MANAGE = "USER_MANAGE";
    public const string ROLE_VIEW   = "ROLE_VIEW";
    public const string ROLE_MANAGE = "ROLE_MANAGE";

    public sealed record Definition(string Code, string Name, string Module, string Description);

    public static readonly IReadOnlyList<Definition> Catalog =
    [
        new(USER_VIEW,   "View users",   "User Management", "List and view users of the tenant."),
        new(USER_MANAGE, "Manage users", "User Management", "Create, edit, deactivate and delete users; assign roles; reset passwords."),
        new(ROLE_VIEW,   "View roles",   "Role Management", "List roles and view their permissions."),
        new(ROLE_MANAGE, "Manage roles", "Role Management", "Create and edit roles and change their permissions."),
    ];
}
