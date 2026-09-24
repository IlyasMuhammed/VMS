using Microsoft.AspNetCore.Mvc;
using VMS.Modules.BusinessPartners.Models;
using VMS.Modules.BusinessPartners.Services;
using VMS.Shared.Authorization;
using VMS.Shared.Pagination;
using VMS.Shared.Partners;

namespace VMS.Modules.BusinessPartners.Controllers;

/// <summary>Business partners: the customers, drivers, workshops, banks and vendors every other module points at.</summary>
[ApiController]
[Route("api/partners")]
public class PartnersController(IPartnerService partners, IPartnerQueryService queries) : ControllerBase
{
    private PartnerCaller Caller => new(
        User.GetUserId(),
        User.FindFirst("user_name")?.Value,
        User.IsSuperAdmin(),
        User.Claims.Where(c => c.Type == "permission").Select(c => c.Value).ToHashSet());

    [HttpGet]
    [RequirePermission(PermissionCodes.BP_VIEW)]
    public async Task<IActionResult> List([FromQuery] PartnerListQuery query) =>
        Ok(ApiResponse<PaginatedResponse<PartnerListItem>>.Ok(await queries.ListAsync(query)));

    /// <summary>Active partners in a role, for other screens' dropdowns.</summary>
    [HttpGet("picker")]
    [RequirePermission(PermissionCodes.BP_VIEW)]
    public async Task<IActionResult> Picker([FromQuery] string? role, [FromQuery] string? search, [FromQuery] int take = 50) =>
        Ok(ApiResponse<List<PartnerPickerItem>>.Ok(await queries.PickerAsync(role, search, take)));

    /// <summary>The filtered list as an Excel file. Columns the caller may not see are left out.</summary>
    [HttpGet("export")]
    [RequirePermission(PermissionCodes.BP_EXPORT)]
    public async Task<IActionResult> Export([FromQuery] PartnerListQuery query)
    {
        var (content, fileName) = await queries.ExportAsync(query, User);
        Response.Headers.CacheControl = "no-store";
        return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    [HttpGet("{id:int}")]
    [RequirePermission(PermissionCodes.BP_VIEW)]
    public async Task<IActionResult> Get(int id) => Ok(ApiResponse<PartnerModel>.Ok(await partners.GetAsync(id)));

    [HttpPost]
    [RequirePermission(PermissionCodes.BP_CREATE)]
    public async Task<IActionResult> Create([FromBody] CreatePartnerRequest request)
    {
        var created = await partners.CreateAsync(request, Caller);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, ApiResponse<PartnerModel>.Ok(created, "Partner saved."));
    }

    [HttpPut("{id:int}")]
    [RequirePermission(PermissionCodes.BP_EDIT)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdatePartnerRequest request) =>
        Ok(ApiResponse<PartnerModel>.Ok(await partners.UpdateAsync(id, request, Caller), "Partner saved."));

    /// <summary>Partners that look like the one being entered, while the form is filled in. For anyone who can create or edit.</summary>
    [HttpPost("duplicate-check")]
    [AuthenticatedOnly]
    public async Task<IActionResult> DuplicateCheck([FromBody] DuplicateCheckRequest request)
    {
        if (!User.HasPermission(PermissionCodes.BP_CREATE) && !User.HasPermission(PermissionCodes.BP_EDIT)) return Forbid();
        return Ok(ApiResponse<DuplicateCheckResult>.Ok(await partners.CheckDuplicatesAsync(request)));
    }

    [HttpPost("{id:int}/roles")]
    [RequirePermission(PermissionCodes.BP_ROLE_MANAGE)]
    public async Task<IActionResult> AddRole(int id, [FromBody] AddRoleRequest request) =>
        Ok(ApiResponse<PartnerModel>.Ok(await partners.AddRoleAsync(id, request, Caller), "Role added."));

    [HttpDelete("{id:int}/roles/{roleCode}")]
    [RequirePermission(PermissionCodes.BP_ROLE_MANAGE)]
    public async Task<IActionResult> RemoveRole(int id, string roleCode, [FromQuery] string? reason) =>
        Ok(ApiResponse<PartnerModel>.Ok(await partners.RemoveRoleAsync(id, roleCode, reason, Caller), "Role removed."));

    [HttpPost("{id:int}/status")]
    [RequirePermission(PermissionCodes.BP_STATUS_CHANGE)]
    public async Task<IActionResult> ChangeStatus(int id, [FromBody] ChangeStatusRequest request) =>
        Ok(ApiResponse<PartnerModel>.Ok(await partners.ChangeStatusAsync(id, request, Caller), "Status changed."));

    /// <summary>The History tab: every change to the partner and what belongs to it, newest first, with the role and status logs.</summary>
    [HttpGet("{id:int}/history")]
    [RequirePermission(PermissionCodes.BP_VIEW)]
    public async Task<IActionResult> History(int id, [FromQuery] int page = 1, [FromQuery] int pageSize = 50) =>
        Ok(ApiResponse<PartnerHistory>.Ok(await queries.HistoryAsync(id, page, pageSize, User)));

    /// <summary>What would stop a role being removed or the partner being set Inactive, so the screen can say so before asking.</summary>
    [HttpGet("{id:int}/usage")]
    [RequirePermission(PermissionCodes.BP_VIEW)]
    public async Task<IActionResult> Usage(int id, [FromQuery] PartnerIntent intent = PartnerIntent.Deactivate, [FromQuery] string? role = null)
    {
        await partners.GetAsync(id);   // a 404 for someone else's partner, not an empty list
        return Ok(ApiResponse<List<PartnerUsageModel>>.Ok(await partners.UsageAsync(id, intent, role)));
    }
}
