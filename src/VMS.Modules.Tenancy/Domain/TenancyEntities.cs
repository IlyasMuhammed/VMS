namespace VMS.Modules.Tenancy.Domain;

/// <summary>
/// A customer of the platform. Every tenant-scoped row in every module carries the id of the
/// tenant that owns it. The platform's own tenant (<c>VMS-PLATFORM</c>) is seeded at startup and
/// is where the Super Admin lives.
/// </summary>
internal class Tenant
{
    public Guid Id { get; set; }
    public string TenantCode { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public string? Address { get; set; }
    public string? Country { get; set; }
    public string? TimeZone { get; set; }
    public int CreatedBy { get; set; }
    public DateTime CreatedDate { get; set; }
    public int? ModifiedBy { get; set; }
    public DateTime? ModifiedDate { get; set; }
}

/// <summary>
/// The authoritative record of platform Super Admins. Deliberately has no TenantId — Super Admin is
/// outside tenant scope by definition. UserId is a plain int with no cross-schema foreign key
/// (Tenancy and Auth are separate modules that share a database, not a constraint).
/// </summary>
internal class SuperAdminUser
{
    public int UserId { get; set; }
    public DateTime CreatedAt { get; set; }
}
