using Microsoft.AspNetCore.Mvc;
using VMS.Modules.Core.Models;
using VMS.Modules.Core.Services;
using VMS.Shared.Authorization;
using VMS.Shared.Pagination;

namespace VMS.Modules.Core.Controllers;

/// <summary>The tenant's numbering series master: prefix, digit count and reset period per kind of record.</summary>
[ApiController]
[Route("api/admin/number-series")]
[RequirePermission(PermissionCodes.ADM_SERIES_MANAGE)]
public class NumberSeriesController(INumberSeriesAdminService series) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List() =>
        Ok(ApiResponse<List<NumberSeriesModel>>.Ok(await series.ListAsync()));

    /// <summary>Applies to numbers issued from now on; numbers already issued keep the form they were issued in.</summary>
    [HttpPut("{code}")]
    public async Task<IActionResult> Update(string code, [FromBody] UpdateNumberSeriesRequest request) =>
        Ok(ApiResponse<NumberSeriesModel>.Ok(await series.UpdateAsync(code, request), "Numbering series saved."));
}
