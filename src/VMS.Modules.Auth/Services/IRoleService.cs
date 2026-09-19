using VMS.Modules.Auth.Models;

namespace VMS.Modules.Auth.Services;

public interface IRoleService
{
    Task<List<RoleListItemModel>> ListAsync();
    Task<RoleDetailModel> GetAsync(int roleId);
    Task<RoleDetailModel> CreateAsync(CreateRoleRequest request, CallerContext caller);
    Task UpdateAsync(int roleId, UpdateRoleRequest request, CallerContext caller);
    Task ReplacePermissionsAsync(int roleId, ReplacePermissionsRequest request, CallerContext caller);
    Task DeactivateAsync(int roleId, CallerContext caller);
    Task<List<RoleUserModel>> GetUsersAsync(int roleId);
    Task<List<PermissionGroupModel>> GetPermissionCatalogAsync();
}
