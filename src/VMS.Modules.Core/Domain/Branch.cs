using VMS.Shared.Common;

namespace VMS.Modules.Core.Domain;

/// <summary>
/// One of the tenant's own locations (FSD §6 field 15, §19 field 19): what a partner or a vehicle belongs to, and, from Stage 6,
/// what a user's row-level visibility is scoped to. Not seeded from a platform catalogue like <see cref="LookupValue"/>: a tenant
/// starts with exactly one, "Head Office" (OQ-10, answered 2026-09-21 — one branch for now), created the first time any tenant
/// asks for its branches. Never deleted: a partner or vehicle recorded against one keeps pointing at it.
/// </summary>
internal class Branch : ITenantScopedEntity
{
    public Guid BranchId { get; set; }
    public Guid TenantId { get; set; }
    /// <summary>Stable and unique within the tenant.</summary>
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedOn { get; set; }
}
