using VMS.Modules.Tenancy.Models;
using VMS.Shared.Pagination;

namespace VMS.Modules.Tenancy.Services;

public interface ITenantService
{
    Task<PaginatedResponse<TenantListItemModel>> GetTenantsAsync(TenantFilter filter);
    Task<TenantDetailModel?> GetTenantAsync(Guid id);
    Task<CreateTenantResult> CreateTenantWithAdminAsync(CreateTenantRequest request, int createdBy);
    Task<bool> UpdateTenantAsync(Guid id, UpdateTenantRequest request, int modifiedBy);

    /// <summary>Activates or deactivates. Deactivation also revokes every session in the tenant.</summary>
    Task<bool> SetStatusAsync(Guid id, bool isActive, int modifiedBy);
}
