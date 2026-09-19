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

    /// <summary>
    /// Stores or replaces the tenant's logo for one mode ("light" or "dark"). Returns false when the
    /// tenant does not exist. Rejects files that are too large or are not PNG, JPEG or WebP.
    /// </summary>
    Task<bool> SetLogoAsync(Guid tenantId, string variant, byte[] content, int updatedBy);

    /// <summary>Null when the tenant has no logo for that mode.</summary>
    Task<TenantLogoFile?> GetLogoAsync(Guid tenantId, string variant);

    /// <summary>Removes the logo for one mode. Returns false only when the tenant does not exist.</summary>
    Task<bool> DeleteLogoAsync(Guid tenantId, string variant);
}
