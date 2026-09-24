namespace VMS.Shared.Tenancy;

/// <summary>
/// What a background job needs to reach every tenant: the platform-wide list a request never asks for (a request already knows its
/// own tenant from the JWT). Implemented by the Tenancy module, consumed by anything that must run once per tenant, unattended.
/// </summary>
public interface ITenantDirectory
{
    /// <summary>Every active tenant's id, for a nightly job to loop over. Order is not meaningful.</summary>
    Task<IReadOnlyList<Guid>> ActiveTenantIdsAsync(CancellationToken cancellationToken = default);
}
