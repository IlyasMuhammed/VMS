namespace VMS.Shared.Common;

public sealed record TenantAdminInvite(int UserId, string InviteLink);

/// <summary>
/// The Auth-side operations Tenancy needs when creating or deactivating a tenant. Defined here so
/// Tenancy never references Auth; implemented in VMS.Modules.Auth and resolved from DI.
/// </summary>
public interface ITenantUserProvisioningService
{
    Task<bool> EmailExistsAsync(string email);

    /// <summary>Creates the tenant's first admin as an invited (inactive, no password) user and emails the invite.</summary>
    Task<TenantAdminInvite> CreateTenantAdminAsync(Guid tenantId, string firstName, string? lastName, string email, int createdBy);

    /// <summary>Revokes every session of every user in the tenant so deactivation takes effect immediately.</summary>
    Task RevokeSessionsForTenantAsync(Guid tenantId);
}
