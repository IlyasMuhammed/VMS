using Microsoft.AspNetCore.Mvc;
using VMS.Modules.Trips.Models;
using VMS.Modules.Trips.Services;
using VMS.Shared.Authorization;
using VMS.Shared.Pagination;

namespace VMS.Modules.Trips.Controllers;

/// <summary>Trip configurations, stops and allowed vehicles (FSD §18, §19). View — <see cref="PermissionCodes.TRP_TRIPCONFIG_VIEW"/>
/// (Admin, Fleet Manager, Operations, Finance); edit — <see cref="PermissionCodes.TRP_TRIPCONFIG_EDIT"/> (Admin, Fleet Manager).</summary>
[ApiController]
[Route("api")]
public sealed class TripConfigurationController(ITripConfigurationService configurations) : ControllerBase
{
    [HttpGet("customers/{customerId:int}/trip-configurations")]
    [RequirePermission(PermissionCodes.TRP_TRIPCONFIG_VIEW)]
    public async Task<IActionResult> List(int customerId, [FromQuery] bool includeInactive = false) =>
        Ok(ApiResponse<IReadOnlyList<TripConfigurationModel>>.Ok(await configurations.ListAsync(customerId, includeInactive)));

    [HttpGet("trip-configurations/{configurationId:long}")]
    [RequirePermission(PermissionCodes.TRP_TRIPCONFIG_VIEW)]
    public async Task<IActionResult> Get(long configurationId) => Ok(ApiResponse<TripConfigurationModel>.Ok(await configurations.GetAsync(configurationId)));

    [HttpPost("trip-configurations")]
    [RequirePermission(PermissionCodes.TRP_TRIPCONFIG_EDIT)]
    public async Task<IActionResult> Create([FromBody] CreateTripConfigurationRequest request) =>
        Ok(ApiResponse<TripConfigurationModel>.Ok(await configurations.CreateAsync(request)));

    [HttpPut("trip-configurations/{configurationId:long}")]
    [RequirePermission(PermissionCodes.TRP_TRIPCONFIG_EDIT)]
    public async Task<IActionResult> Update(long configurationId, [FromBody] UpdateTripConfigurationRequest request) =>
        Ok(ApiResponse<TripConfigurationModel>.Ok(await configurations.UpdateAsync(configurationId, request)));

    [HttpPut("trip-configurations/{configurationId:long}/stops")]
    [RequirePermission(PermissionCodes.TRP_TRIPCONFIG_EDIT)]
    public async Task<IActionResult> UpdateStops(long configurationId, [FromBody] UpdateTripConfigurationStopsRequest request) =>
        Ok(ApiResponse<TripConfigurationModel>.Ok(await configurations.UpdateStopsAsync(configurationId, request)));

    [HttpPost("trip-configurations/{configurationId:long}/activate")]
    [RequirePermission(PermissionCodes.TRP_TRIPCONFIG_EDIT)]
    public async Task<IActionResult> Activate(long configurationId, [FromBody] ChangeTripConfigurationStatusRequest request) =>
        Ok(ApiResponse<TripConfigurationModel>.Ok(await configurations.ActivateAsync(configurationId, request)));

    [HttpPost("trip-configurations/{configurationId:long}/deactivate")]
    [RequirePermission(PermissionCodes.TRP_TRIPCONFIG_EDIT)]
    public async Task<IActionResult> Deactivate(long configurationId, [FromBody] ChangeTripConfigurationStatusRequest request) =>
        Ok(ApiResponse<TripConfigurationModel>.Ok(await configurations.DeactivateAsync(configurationId, request)));

    [HttpPost("trip-configurations/{configurationId:long}/copy")]
    [RequirePermission(PermissionCodes.TRP_TRIPCONFIG_EDIT)]
    public async Task<IActionResult> Copy(long configurationId, [FromBody] CopyTripConfigurationRequest request) =>
        Ok(ApiResponse<TripConfigurationModel>.Ok(await configurations.CopyAsync(configurationId, request)));

    [HttpGet("trip-configurations/{configurationId:long}/vehicles")]
    [RequirePermission(PermissionCodes.TRP_TRIPCONFIG_VIEW)]
    public async Task<IActionResult> ListVehicles(long configurationId) =>
        Ok(ApiResponse<IReadOnlyList<TripConfigurationVehicleModel>>.Ok(await configurations.ListVehiclesAsync(configurationId)));

    [HttpPost("trip-configurations/{configurationId:long}/vehicles")]
    [RequirePermission(PermissionCodes.TRP_TRIPCONFIG_EDIT)]
    public async Task<IActionResult> AssignVehicle(long configurationId, [FromBody] AssignTripConfigurationVehicleRequest request) =>
        Ok(ApiResponse<TripConfigurationVehicleModel>.Ok(await configurations.AssignVehicleAsync(configurationId, request)));

    [HttpPut("trip-configuration-vehicles/{assignmentId:long}")]
    [RequirePermission(PermissionCodes.TRP_TRIPCONFIG_EDIT)]
    public async Task<IActionResult> UpdateVehicle(long assignmentId, [FromBody] UpdateTripConfigurationVehicleRequest request) =>
        Ok(ApiResponse<TripConfigurationVehicleModel>.Ok(await configurations.UpdateVehicleAsync(assignmentId, request)));
}
