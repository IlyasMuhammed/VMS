using VMS.Shared.Common;

namespace VMS.Modules.Auth.Models;

// ── Authentication ───────────────────────────────────────────────────────────

public class LoginRequestModel
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class LoginResponseModel
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    /// <summary>Seconds until the access token expires — derived from the token's real lifetime.</summary>
    public int ExpiresIn { get; set; }
    public CurrentUserModel User { get; set; } = null!;
}

public class RefreshRequestModel
{
    public string RefreshToken { get; set; } = string.Empty;
}

public class RefreshResponseModel
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public int ExpiresIn { get; set; }
}

public class LogoutRequestModel
{
    public string RefreshToken { get; set; } = string.Empty;
}

public class AcceptInviteRequest
{
    public string Token { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

public class ForgotPasswordRequest
{
    public string Email { get; set; } = string.Empty;
}

public class ResetPasswordRequest
{
    public string Email { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

public class ChangePasswordRequest
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

public class CurrentUserModel
{
    public int UserId { get; set; }
    public Guid TenantId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string? LastName { get; set; }
    public string? Phone { get; set; }
    public string? Department { get; set; }
    public DropDownVM Role { get; set; } = null!;
    public bool IsSuperAdmin { get; set; }
    public List<string> Permissions { get; set; } = [];
}

// ── User management ──────────────────────────────────────────────────────────

public class CreateUserRequest
{
    public string FirstName { get; set; } = string.Empty;
    public string? LastName { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Department { get; set; }
    public int RoleId { get; set; }
}

public class UserListFilter
{
    public int? RoleId { get; set; }
    /// <summary>"active" | "inactive"</summary>
    public string? Status { get; set; }
    public string? Department { get; set; }
    /// <summary>Partial match on name or email.</summary>
    public string? Search { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class UserListItemModel
{
    public int UserId { get; set; }
    public Guid TenantId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string? LastName { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? Department { get; set; }
    public bool IsActive { get; set; }
    /// <summary>True while the user has an outstanding invite / password-reset link.</summary>
    public bool InvitePending { get; set; }
    public DropDownVM? Role { get; set; }
    public DateTime CreatedDate { get; set; }
    public DateTime? LastLoginAt { get; set; }
}

public class UserDetailModel : UserListItemModel
{
    public string? Phone { get; set; }
    /// <summary>
    /// Only populated on create and on admin reset: the one-time link the user follows to set a
    /// password. Handed over here so onboarding works when outbound email is not configured.
    /// </summary>
    public string? InviteLink { get; set; }
}

public class PatchUserRequest
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Phone { get; set; }
    public string? Department { get; set; }
    public bool? IsActive { get; set; }
}

public class AssignRoleRequest
{
    public int RoleId { get; set; }
}

// ── Role management ──────────────────────────────────────────────────────────

/// <summary>A role the caller may hand out — see <c>GET /api/users/assignable-roles</c>.</summary>
public class RoleOptionModel
{
    public int RoleId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string RoleCode { get; set; } = string.Empty;
}

public class RoleListItemModel
{
    public int RoleId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string RoleCode { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    /// <summary>Shared platform role (editable only by a Super Admin) vs. a tenant's own custom role.</summary>
    public bool IsGlobal { get; set; }
    public int ActiveUserCount { get; set; }
    public int PermissionCount { get; set; }
}

public class RoleDetailModel : RoleListItemModel
{
    public List<PermissionGroupModel> PermissionGroups { get; set; } = [];
}

public class PermissionGroupModel
{
    public string Module { get; set; } = string.Empty;
    public List<PermissionItemModel> Permissions { get; set; } = [];
}

public class PermissionItemModel
{
    public int PermissionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsAllowed { get; set; }
}

public class CreateRoleRequest
{
    public string Name { get; set; } = string.Empty;
    public string RoleCode { get; set; } = string.Empty;
    public string? Description { get; set; }
    /// <summary>Honoured only for a Super Admin; everyone else creates a role private to their tenant.</summary>
    public bool IsGlobal { get; set; }
}

public class UpdateRoleRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
}

public class ReplacePermissionsRequest
{
    public List<int> AllowedPermissionIds { get; set; } = [];
}

public class RoleUserModel
{
    public int UserId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string? LastName { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? Department { get; set; }
    public bool IsActive { get; set; }
}
