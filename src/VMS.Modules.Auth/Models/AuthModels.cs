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
    /// <summary>§23B.4. Defaults to All branches when not given.</summary>
    public string? ScopeType { get; set; }
    public Guid? BranchId { get; set; }
    /// <summary>§23B.1: the Business Partner this user is the same person as, when they are also a driver (OQ-20).</summary>
    public int? LinkedPartnerId { get; set; }
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
    /// <summary>The primary role's data scope (§23B.4).</summary>
    public string ScopeType { get; set; } = string.Empty;
    public Guid? BranchId { get; set; }
    public string? BranchName { get; set; }
}

public class UserDetailModel : UserListItemModel
{
    public string? Phone { get; set; }
    /// <summary>
    /// Only populated on create and on admin reset: the one-time link the user follows to set a
    /// password. Handed over here so onboarding works when outbound email is not configured.
    /// </summary>
    public string? InviteLink { get; set; }
    public int? LinkedPartnerId { get; set; }
    public string? LinkedPartnerName { get; set; }
    /// <summary>Every role this user holds, beyond the primary one (§23B.1: effective permission is the union).</summary>
    public List<UserRoleModel> Roles { get; set; } = [];
}

/// <summary>One role a user holds, with its own scope — for the "why can they see that" effective-permissions viewer (§23B.6).</summary>
public class UserRoleModel
{
    public int RoleId { get; set; }
    public string RoleName { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
    public string ScopeType { get; set; } = string.Empty;
    public Guid? BranchId { get; set; }
    public string? BranchName { get; set; }
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

/// <summary>Adds a role the user holds alongside their existing ones, each with its own scope (§23B.1, §23B.4).</summary>
public class AddRoleRequest
{
    public int RoleId { get; set; }
    public string? ScopeType { get; set; }
    public Guid? BranchId { get; set; }
}

/// <summary>Sets the primary role's data scope (§23B.4).</summary>
public class SetScopeRequest
{
    public string ScopeType { get; set; } = string.Empty;
    public Guid? BranchId { get; set; }
}

/// <summary>Links or unlinks the Business Partner this user is the same person as (§23B.1, OQ-20). Null clears the link.</summary>
public class SetDriverLinkRequest
{
    public int? PartnerId { get; set; }
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
    /// <summary>Interface, Operation or Field (§23B) — the permission tree editor's second grouping level.</summary>
    public string Level { get; set; } = string.Empty;
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

/// <summary>One capability a user effectively holds, and every role of theirs that grants it (§23B.6: "why can they see that?").</summary>
public class EffectivePermissionModel
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Module { get; set; } = string.Empty;
    public string Level { get; set; } = string.Empty;
    public List<string> GrantedByRoles { get; set; } = [];
}
