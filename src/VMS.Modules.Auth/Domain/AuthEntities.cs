using VMS.Shared.Common;

namespace VMS.Modules.Auth.Domain;

internal class UserAccount : ITenantScopedEntity
{
    public int UserID { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string? LastName { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Department { get; set; }
    public int RoleID { get; set; }
    public bool IsActive { get; set; }
    public bool IsDeleted { get; set; }
    public int CreatedBy { get; set; }
    public DateTime CreatedDate { get; set; }
    public DateTime? UpdatedDate { get; set; }
    public DateTime? LastLoginAt { get; set; }

    // Account lockout tracking
    public int FailedLoginAttempts { get; set; }
    public DateTime? LastFailedAt { get; set; }
    public DateTime? LockedUntil { get; set; }

    // Forgot-password flow: a 6-digit code emailed to the user. Only its SHA-256 is stored, and the
    // code dies after a few wrong guesses so it cannot be ground through.
    public string? PasswordResetCodeHash { get; set; }
    public DateTime? PasswordResetCodeExpiresAt { get; set; }
    public int PasswordResetAttempts { get; set; }

    // Invite flow: the emailed link carries a random token; only its SHA-256 is stored. Used for a
    // new user's first password and for an admin-initiated password reset. Cleared once accepted.
    public string? InviteTokenHash { get; set; }
    public DateTime? InviteTokenExpiresAt { get; set; }

    /// <summary>Owning tenant. No cross-schema FK — Tenancy owns the Tenant table.</summary>
    public Guid TenantId { get; set; }
}

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

internal class Permission
{
    public int PermissionID { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Module { get; set; } = string.Empty;
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

/// <summary>Seeded role codes that the application logic refers to.</summary>
internal static class RoleCodes
{
    public const string SuperAdmin = "SUPER_ADMIN";
    public const string TenantAdmin = "TENANT_ADMIN";
    public const string Manager = "MANAGER";
    public const string Staff = "STAFF";
}
