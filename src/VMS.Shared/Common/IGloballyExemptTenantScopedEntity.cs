namespace VMS.Shared.Common;

/// <summary>
/// For entities that are either a shared platform row (<c>IsGlobal = true</c>, no tenant — e.g. the
/// seeded system roles) or a tenant-owned custom row (<c>IsGlobal = false</c>, <c>TenantId</c> set).
/// Kept distinct from <see cref="ITenantScopedEntity"/> because <c>TenantId</c> is nullable here.
/// </summary>
public interface IGloballyExemptTenantScopedEntity
{
    Guid? TenantId { get; set; }
    bool IsGlobal { get; set; }
}
