using VMS.Shared.Auditing;
using VMS.Shared.Authorization;
using VMS.Shared.Common;

namespace VMS.Modules.Auth.Domain;

// Audited: who the user is, what role they hold, whether they are active. Not audited: secrets and the
// counters that change on every sign-in, which would bury the entries that matter.
internal class UserAccount : ITenantScopedEntity
{
    public int UserID { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string? LastName { get; set; }
    public string Email { get; set; } = string.Empty;
    [NotAudited] public string PasswordHash { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Department { get; set; }
    public int RoleID { get; set; }
    /// <summary>§23B.1: "linked Business Partner where the user is also a driver" (OQ-20: same role system). No FK — <see cref="VMS.Shared.Partners.IPartnerDirectory"/> validates it, and it must carry the Driver role.</summary>
    public int? LinkedPartnerId { get; set; }
    public bool IsActive { get; set; }
    public bool IsDeleted { get; set; }
    public int CreatedBy { get; set; }
    public DateTime CreatedDate { get; set; }
    [NotAudited] public DateTime? UpdatedDate { get; set; }
    [NotAudited] public DateTime? LastLoginAt { get; set; }

    // Account lockout tracking
    [NotAudited] public int FailedLoginAttempts { get; set; }
    [NotAudited] public DateTime? LastFailedAt { get; set; }
    [NotAudited] public DateTime? LockedUntil { get; set; }

    /// <summary>
    /// Every role the user holds, each with its own data scope. <see cref="RoleID"/> stays as the
    /// primary role (what the sign-in token and the users list use) until multi-role assignment ships.
    /// </summary>
    public List<UserRole> UserRoles { get; } = [];

    // Forgot-password flow: a 6-digit code emailed to the user. Only its SHA-256 is stored, and the
    // code dies after a few wrong guesses so it cannot be ground through.
    [NotAudited] public string? PasswordResetCodeHash { get; set; }
    [NotAudited] public DateTime? PasswordResetCodeExpiresAt { get; set; }
    [NotAudited] public int PasswordResetAttempts { get; set; }

    // Invite flow: the emailed link carries a random token; only its SHA-256 is stored. Used for a
    // new user's first password and for an admin-initiated password reset. Cleared once accepted.
    [NotAudited] public string? InviteTokenHash { get; set; }
    [NotAudited] public DateTime? InviteTokenExpiresAt { get; set; }

    /// <summary>Owning tenant. No cross-schema FK — Tenancy owns the Tenant table.</summary>
    public Guid TenantId { get; set; }
}

/// <summary>A refresh-token session. Rotates on every refresh, so it is not audited.</summary>
[NotAudited]
internal class UserSession : ITenantScopedEntity
{
    public Guid Id { get; set; }
    public int UserID { get; set; }
    /// <summary>SHA-256 hex of the raw refresh token — the raw value is never stored.</summary>
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public Guid TenantId { get; set; }
}

/// <summary>The permission catalogue. It is managed by code and the seeder, never by users, so it is not audited.</summary>
[NotAudited]
internal class Permission
{
    public int PermissionID { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Module { get; set; } = string.Empty;
    /// <summary>Interface, Operation or Field. See <see cref="VMS.Shared.Authorization.PermissionLevels"/>.</summary>
    public string Level { get; set; } = "Operation";
    public string? Description { get; set; }
}

/// <summary>
/// Either a global role (<c>IsGlobal</c>, no tenant — the seeded catalog every tenant can assign)
/// or a tenant-owned custom role (private to and editable only by the tenant that created it).
/// </summary>
internal class Role : IGloballyExemptTenantScopedEntity
{
    public int RoleID { get; set; }
    public string Name { get; set; } = string.Empty;
    public string RoleCode { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsGlobal { get; set; } = true;
    public Guid? TenantId { get; set; }
}

/// <summary>A row means the role holds the permission. Reached only through a Role, so it needs no filter of its own.</summary>
internal class RolePermission
{
    public int RolePermissionID { get; set; }
    public int RoleID { get; set; }
    public int PermissionID { get; set; }
}

/// <summary>
/// A role held by a user, and on which records it applies (FSD §23B.4). The same Finance role can be
/// all-branch for head office and single-branch for a regional user, because scope lives here and not
/// on the role.
/// </summary>
internal class UserRole : ITenantScopedEntity
{
    public int UserRoleID { get; set; }
    public int UserID { get; set; }
    public int RoleID { get; set; }
    public string ScopeType { get; set; } = ScopeTypes.AllBranches;
    /// <summary>The branch for <see cref="ScopeTypes.OwnBranch"/>. No foreign key: branches live in another module.</summary>
    public Guid? BranchId { get; set; }
    public Guid TenantId { get; set; }
}

/// <summary>A refused request (BR-SEC-007). Append-only in practice; nothing edits or deletes these. It is a log, so it is not audited.</summary>
[NotAudited]
internal class AccessDenialRecord : ITenantScopedEntity
{
    public long AccessDenialID { get; set; }
    public DateTime OccurredAt { get; set; }
    public Guid TenantId { get; set; }
    public int UserId { get; set; }
    public string? UserName { get; set; }
    public string Permission { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string? RouteValues { get; set; }
    public string? IpAddress { get; set; }
}

/// <summary>Seeded role codes that the application logic refers to.</summary>
internal static class RoleCodes
{
    public const string SuperAdmin = "SUPER_ADMIN";
    public const string TenantAdmin = "TENANT_ADMIN";
    public const string Manager = "MANAGER";
    public const string Staff = "STAFF";

    // §23B.7's default role templates — shipped, fully editable, a starting point for the client's own roles.
    // "Administrator" (Everything) is TenantAdmin above; these five are the rest of the table.
    public const string FleetManager = "FLEET_MANAGER";
    public const string FinanceUser = "FINANCE_USER";
    public const string OperationsUser = "OPERATIONS_USER";
    public const string Driver = "DRIVER";
    public const string ReadOnly = "READ_ONLY";
}
