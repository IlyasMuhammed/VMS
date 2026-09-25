using Microsoft.AspNetCore.Mvc;
using VMS.Modules.Trips.Models;
using VMS.Modules.Trips.Services;
using VMS.Shared.Authorization;
using VMS.Shared.Pagination;

namespace VMS.Modules.Trips.Controllers;

/// <summary>Fuel Card Management (§28). One permission for everything (§28: "Permissions: Admin, Fleet Manager,
/// Finance" — no separate view-only tier, unlike most of this module's other masters).</summary>
[ApiController]
[Route("api/fuel-cards")]
public sealed class FuelCardController(IFuelCardService fuelCards) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCodes.TRP_FUELCARD_EDIT)]
    public async Task<IActionResult> List() => Ok(ApiResponse<IReadOnlyList<FuelCardModel>>.Ok(await fuelCards.ListAsync()));

    [HttpGet("{id:int}")]
    [RequirePermission(PermissionCodes.TRP_FUELCARD_EDIT)]
    public async Task<IActionResult> Get(int id) => Ok(ApiResponse<FuelCardModel>.Ok(await fuelCards.GetAsync(id)));

    [HttpPost]
    [RequirePermission(PermissionCodes.TRP_FUELCARD_EDIT)]
    public async Task<IActionResult> Create([FromBody] CreateFuelCardRequest request) =>
        Ok(ApiResponse<FuelCardModel>.Ok(await fuelCards.CreateAsync(request), "Fuel card created."));

    [HttpPut("{id:int}")]
    [RequirePermission(PermissionCodes.TRP_FUELCARD_EDIT)]
    public async Task<IActionResult> Save(int id, [FromBody] SaveFuelCardRequest request) =>
        Ok(ApiResponse<FuelCardModel>.Ok(await fuelCards.SaveAsync(id, request), "Fuel card saved."));

    [HttpPost("{id:int}/assign")]
    [RequirePermission(PermissionCodes.TRP_FUELCARD_EDIT)]
    public async Task<IActionResult> Assign(int id, [FromBody] AssignFuelCardRequest request) =>
        Ok(ApiResponse<FuelCardModel>.Ok(await fuelCards.AssignAsync(id, request), "Fuel card assigned."));

    [HttpGet("{id:int}/assignments")]
    [RequirePermission(PermissionCodes.TRP_FUELCARD_EDIT)]
    public async Task<IActionResult> Assignments(int id) => Ok(ApiResponse<IReadOnlyList<FuelCardAssignmentModel>>.Ok(await fuelCards.AssignmentsAsync(id)));
}

/// <summary>A manual trigger for the nightly fuel card expiry job, the same pattern S4-REC-05/S5-DOC set for a job with no scheduler.</summary>
[ApiController]
[Route("api/admin/jobs/fuel-cards")]
public sealed class FuelCardJobController(IFuelCardExpiryJob job) : ControllerBase
{
    [HttpPost("run")]
    [RequirePermission(PermissionCodes.ADM_CONFIG_MANAGE)]
    public async Task<IActionResult> Run() => Ok(ApiResponse<FuelCardExpiryResult>.Ok(await job.RunAsync()));
}
