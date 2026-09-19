using Microsoft.AspNetCore.Mvc;
using VMS.Modules.Auth.Models;
using VMS.Modules.Auth.Services;
using VMS.Shared.Authorization;
using VMS.Shared.Pagination;

namespace VMS.Modules.Auth.Controllers;

[ApiController]
[Route("api/roles")]
public class RolesController(IRoleService roles) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCodes.ROLE_VIEW)]
    public async Task<IActionResult> List() =>
        Ok(ApiResponse<List<RoleListItemModel>>.Ok(await roles.ListAsync()));

    /// <summary>Every permission that exists, grouped by module — for building a role's permission picker.</summary>
    [HttpGet("permissions")]
    [RequirePermission(PermissionCodes.ROLE_VIEW)]
    public async Task<IActionResult> PermissionCatalog() =>
        Ok(ApiResponse<List<PermissionGroupModel>>.Ok(await roles.GetPermissionCatalogAsync()));

    [HttpGet("{id:int}")]
    [RequirePermission(PermissionCodes.ROLE_VIEW)]
    public async Task<IActionResult> Get(int id) =>
        Ok(ApiResponse<RoleDetailModel>.Ok(await roles.GetAsync(id)));

    [HttpGet("{id:int}/users")]
    [RequirePermission(PermissionCodes.ROLE_VIEW)]
    public async Task<IActionResult> GetUsers(int id) =>
        Ok(ApiResponse<List<RoleUserModel>>.Ok(await roles.GetUsersAsync(id)));

    [HttpPost]
    [RequirePermission(PermissionCodes.ROLE_MANAGE)]
    public async Task<IActionResult> Create([FromBody] CreateRoleRequest request)
    {
        var created = await roles.CreateAsync(request, User.ToCaller());
        return CreatedAtAction(nameof(Get), new { id = created.RoleId }, ApiResponse<RoleDetailModel>.Ok(created, "Role created."));
    }

    [HttpPut("{id:int}")]
    [RequirePermission(PermissionCodes.ROLE_MANAGE)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateRoleRequest request)
    {
        await roles.UpdateAsync(id, request, User.ToCaller());
        return Ok(ApiResponse.Ok("Role updated."));
    }

    [HttpPut("{id:int}/permissions")]
    [RequirePermission(PermissionCodes.ROLE_MANAGE)]
    public async Task<IActionResult> ReplacePermissions(int id, [FromBody] ReplacePermissionsRequest request)
    {
        await roles.ReplacePermissionsAsync(id, request, User.ToCaller());
        return Ok(ApiResponse.Ok("Role permissions saved."));
    }

    /// <summary>Deactivates the role; refused with 409 while active users still hold it.</summary>
    [HttpPatch("{id:int}/deactivate")]
    [RequirePermission(PermissionCodes.ROLE_MANAGE)]
    public async Task<IActionResult> Deactivate(int id)
    {
        await roles.DeactivateAsync(id, User.ToCaller());
        return Ok(ApiResponse.Ok("Role deactivated."));
    }
}
