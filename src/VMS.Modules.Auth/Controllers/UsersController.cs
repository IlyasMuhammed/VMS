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

    /// <summary>Adds a role alongside the ones the user already holds (§23B.1: effective permission is the union).</summary>
    [HttpPost("{id:int}/roles")]
    [RequirePermission(PermissionCodes.USER_MANAGE)]
    public async Task<IActionResult> AddRole(int id, [FromBody] AddRoleRequest request)
    {
        await users.AddRoleAsync(id, request, User.ToCaller());
        return Ok(ApiResponse.Ok("Role added."));
    }

    [HttpDelete("{id:int}/roles/{roleId:int}")]
    [RequirePermission(PermissionCodes.USER_MANAGE)]
    public async Task<IActionResult> RemoveRole(int id, int roleId)
    {
        await users.RemoveRoleAsync(id, roleId, User.ToCaller());
        return Ok(ApiResponse.Ok("Role removed."));
    }

    /// <summary>§23B.4: the primary role's data scope.</summary>
    [HttpPut("{id:int}/scope")]
    [RequirePermission(PermissionCodes.USER_MANAGE)]
    public async Task<IActionResult> SetScope(int id, [FromBody] SetScopeRequest request)
    {
        await users.SetScopeAsync(id, request, User.ToCaller());
        return Ok(ApiResponse.Ok("Scope saved."));
    }

    /// <summary>§23B.1, OQ-20: the Business Partner this user is the same person as, when they are also a driver.</summary>
    [HttpPut("{id:int}/driver-link")]
    [RequirePermission(PermissionCodes.USER_MANAGE)]
    public async Task<IActionResult> SetDriverLink(int id, [FromBody] SetDriverLinkRequest request)
    {
        await users.SetDriverLinkAsync(id, request, User.ToCaller());
        return Ok(ApiResponse.Ok("Driver link saved."));
    }

    /// <summary>§23B.6: "why can they see that?" — the flattened union of every permission the user holds, and which role(s) grant each one.</summary>
    [HttpGet("{id:int}/effective-permissions")]
    [RequirePermission(PermissionCodes.USER_VIEW)]
    public async Task<IActionResult> EffectivePermissions(int id) =>
        Ok(ApiResponse<List<EffectivePermissionModel>>.Ok(await users.GetEffectivePermissionsAsync(id)));

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
