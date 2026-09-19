namespace VMS.Shared.Common;

/// <summary>
/// Implemented by every tenant-owned entity. Drives both the stamp-on-create
/// (<see cref="TenantScopingExtensions.StampTenantScopedEntities"/>) and the global query filter
/// (<see cref="TenantScopingExtensions.ApplyTenantQueryFilters{TContext}"/>).
/// </summary>
public interface ITenantScopedEntity
{
    Guid TenantId { get; set; }
}
