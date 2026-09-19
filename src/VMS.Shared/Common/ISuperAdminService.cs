namespace VMS.Shared.Common;

/// <summary>Platform Super Admin membership, owned by Tenancy. Consumed by Auth when issuing tokens and by the seeder.</summary>
public interface ISuperAdminService
{
    Task<bool> IsSuperAdminAsync(int userId);
    Task GrantAsync(int userId);
}
