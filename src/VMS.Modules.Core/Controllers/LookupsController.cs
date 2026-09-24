using Microsoft.AspNetCore.Mvc;
using VMS.Modules.Core.Models;
using VMS.Modules.Core.Services;
using VMS.Shared.Authorization;
using VMS.Shared.Lookups;
using VMS.Shared.Pagination;

namespace VMS.Modules.Core.Controllers;

/// <summary>The lists that fill dropdowns. Any signed-in user may read them: they hold no personal or financial data.</summary>
[ApiController]
[Route("api/lookups")]
[AuthenticatedOnly]
public class LookupsController(ILookupReader lookups) : ControllerBase
{
    /// <summary>
    /// The values that can be chosen now, in the order to show them. Retired values are left out. Any other
    /// query parameter filters on a field of the list: <c>/api/lookups/CITY?province=PUNJAB</c>.
    /// </summary>
    [HttpGet("{lookupType}")]
    public async Task<IActionResult> Active(string lookupType)
    {
        var filter = Request.Query.ToDictionary(q => q.Key, q => q.Value.ToString());
        return Ok(ApiResponse<IReadOnlyList<LookupItem>>.Ok(await lookups.GetActiveAsync(lookupType, filter)));
    }

    /// <summary>
    /// One value, retired or not, so a screen can still label the value a record was saved with after it was
    /// retired. Someone else's tenant, another list's id, or an unknown id is a plain 404.
    /// </summary>
    [HttpGet("{lookupType}/{id:int}")]
    public async Task<IActionResult> One(string lookupType, int id)
    {
        var item = await lookups.FindAsync(lookupType, id);
        return item is null ? NotFound(ApiResponse.Fail("Value not found.")) : Ok(ApiResponse<LookupItem>.Ok(item));
    }
}

/// <summary>The tenant's own locations (FSD §6 field 15). Any signed-in user may read them, the same as the platform lists.</summary>
[ApiController]
[Route("api/branches")]
[AuthenticatedOnly]
public class BranchesController(IBranchService branches) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List() => Ok(ApiResponse<List<BranchModel>>.Ok(await branches.ListAsync()));
}

/// <summary>Administration → Master data: the tenant's own lists.</summary>
[ApiController]
[Route("api/admin/lookups")]
[RequirePermission(PermissionCodes.ADM_MASTER_MANAGE)]
public class LookupAdminController(ILookupAdminService admin) : ControllerBase
{
    /// <summary>Every list, with the extra fields its values carry and how many values it has.</summary>
    [HttpGet]
    public async Task<IActionResult> Types() =>
        Ok(ApiResponse<List<LookupTypeModel>>.Ok(await admin.GetTypesAsync()));

    /// <summary>Every value of one list, retired ones included.</summary>
    [HttpGet("{lookupType}")]
    public async Task<IActionResult> Values(string lookupType) =>
        Ok(ApiResponse<List<LookupItem>>.Ok(await admin.GetValuesAsync(lookupType)));

    [HttpPost("{lookupType}")]
    public async Task<IActionResult> Create(string lookupType, [FromBody] CreateLookupRequest request)
    {
        var created = await admin.CreateAsync(lookupType, request);
        return CreatedAtAction(nameof(Values), new { lookupType }, ApiResponse<LookupItem>.Ok(created, "Value added."));
    }

    /// <summary>Renames, reorders, retires or re-flags a value. The code cannot change.</summary>
    [HttpPut("{lookupType}/{id:int}")]
    public async Task<IActionResult> Update(string lookupType, int id, [FromBody] UpdateLookupRequest request) =>
        Ok(ApiResponse<LookupItem>.Ok(await admin.UpdateAsync(lookupType, id, request), "Value saved."));
}
