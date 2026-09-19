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
    Task AssignRoleAsync(int userId, int roleId, CallerContext caller);
    Task<UserDetailModel> ResetPasswordAsync(int userId, CallerContext caller);
    Task DeleteAsync(int userId, CallerContext caller);
}
