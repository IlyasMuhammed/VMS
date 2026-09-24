using VMS.Modules.Auth.Models;
using VMS.Shared.Pagination;

namespace VMS.Modules.Auth.Services;

public interface IUserService
{
    Task<UserDetailModel> CreateAsync(CreateUserRequest request, CallerContext caller);
    Task<PaginatedResponse<UserListItemModel>> ListAsync(UserListFilter filter);
    Task<UserDetailModel> GetAsync(int userId);

    /// <summary>Active roles the caller is allowed to assign — what a role picker should offer.</summary>
    Task<List<RoleOptionModel>> GetAssignableRolesAsync(CallerContext caller);
    Task PatchAsync(int userId, PatchUserRequest request, CallerContext caller);
    /// <summary>Sets the user's one primary role, replacing every role they held (kept for the common single-role case).</summary>
    Task AssignRoleAsync(int userId, int roleId, CallerContext caller);
    /// <summary>Adds a role alongside the ones the user already holds, each with its own scope (§23B.1: effective permission is the union).</summary>
    Task AddRoleAsync(int userId, AddRoleRequest request, CallerContext caller);
    /// <summary>Removes one role the user holds. Refused if it is their only one, or if it would leave the tenant with no active administrator (BR-SEC-008).</summary>
    Task RemoveRoleAsync(int userId, int roleId, CallerContext caller);
    Task SetScopeAsync(int userId, SetScopeRequest request, CallerContext caller);
    Task SetDriverLinkAsync(int userId, SetDriverLinkRequest request, CallerContext caller);
    /// <summary>The flattened union of every permission the user holds across their roles, and which role(s) grant each one (§23B.6).</summary>
    Task<List<EffectivePermissionModel>> GetEffectivePermissionsAsync(int userId);
    Task<UserDetailModel> ResetPasswordAsync(int userId, CallerContext caller);
    Task DeleteAsync(int userId, CallerContext caller);
}
