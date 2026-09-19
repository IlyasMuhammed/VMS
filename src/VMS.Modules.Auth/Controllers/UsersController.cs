using Microsoft.AspNetCore.Mvc;
using VMS.Modules.Auth.Models;
using VMS.Modules.Auth.Services;
using VMS.Shared.Authorization;
using VMS.Shared.Pagination;

namespace VMS.Modules.Auth.Controllers;

[ApiController]
[Route("api/users")]
public class UsersController(IUserService users) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCodes.USER_VIEW)]
    public async Task<IActionResult> List([FromQuery] UserListFilter filter) =>
        Ok(ApiResponse<PaginatedResponse<UserListItemModel>>.Ok(await users.ListAsync(filter)));

    /// <summary>The roles the caller may assign — for role pickers. Needs only USER_VIEW, not ROLE_VIEW.</summary>
    [HttpGet("assignable-roles")]
    [RequirePermission(PermissionCodes.USER_VIEW)]
    public async Task<IActionResult> AssignableRoles() =>
        Ok(ApiResponse<List<RoleOptionModel>>.Ok(await users.GetAssignableRolesAsync(User.ToCaller())));

    [HttpGet("{id:int}")]
    [RequirePermission(PermissionCodes.USER_VIEW)]
    public async Task<IActionResult> Get(int id) =>
        Ok(ApiResponse<UserDetailModel>.Ok(await users.GetAsync(id)));

    /// <summary>Creates the user and returns a one-time invite link for them to set their password.</summary>
    [HttpPost]
    [RequirePermission(PermissionCodes.USER_MANAGE)]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest request)
    {
        var created = await users.CreateAsync(request, User.ToCaller());
        return CreatedAtAction(nameof(Get), new { id = created.UserId }, ApiResponse<UserDetailModel>.Ok(created, "User created."));
    }

    [HttpPatch("{id:int}")]
    [RequirePermission(PermissionCodes.USER_MANAGE)]
    public async Task<IActionResult> Patch(int id, [FromBody] PatchUserRequest request)
    {
        await users.PatchAsync(id, request, User.ToCaller());
        return Ok(ApiResponse.Ok("User updated."));
    }

    [HttpPut("{id:int}/role")]
    [RequirePermission(PermissionCodes.USER_MANAGE)]
    public async Task<IActionResult> AssignRole(int id, [FromBody] AssignRoleRequest request)
    {
        await users.AssignRoleAsync(id, request.RoleId, User.ToCaller());
        return Ok(ApiResponse.Ok("Role assigned."));
    }

    /// <summary>Issues a fresh one-time link for the user to set a new password, and ends their sessions.</summary>
    [HttpPost("{id:int}/reset-password")]
    [RequirePermission(PermissionCodes.USER_MANAGE)]
    public async Task<IActionResult> ResetPassword(int id) =>
        Ok(ApiResponse<UserDetailModel>.Ok(await users.ResetPasswordAsync(id, User.ToCaller()), "Password reset link issued."));

    [HttpDelete("{id:int}")]
    [RequirePermission(PermissionCodes.USER_MANAGE)]
    public async Task<IActionResult> Delete(int id)
    {
        await users.DeleteAsync(id, User.ToCaller());
        return Ok(ApiResponse.Ok("User deleted."));
    }
}
