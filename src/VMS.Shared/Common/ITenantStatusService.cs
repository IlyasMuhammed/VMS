namespace VMS.Shared.Common;

/// <summary>
/// Lets Auth check a user's tenant is active at login without referencing Tenancy. Implemented in
/// VMS.Modules.Tenancy and resolved from DI.
/// </summary>
public interface ITenantStatusService
{
    Task<bool> IsTenantActiveAsync(Guid tenantId);
}
