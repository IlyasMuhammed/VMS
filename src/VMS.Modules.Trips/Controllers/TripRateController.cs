using Microsoft.AspNetCore.Mvc;
using VMS.Modules.Trips.Models;
using VMS.Modules.Trips.Services;
using VMS.Shared.Authorization;
using VMS.Shared.Pagination;

namespace VMS.Modules.Trips.Controllers;

/// <summary>Effective-dated trip rates (FSD §26). View — <see cref="PermissionCodes.TRP_RATE_VIEW"/>; edit —
/// <see cref="PermissionCodes.TRP_RATE_CONFIGURE"/> (Admin, Finance; Fleet Manager only if granted, §44).</summary>
[ApiController]
[Route("api")]
public sealed class TripRateController(ITripRateService rates) : ControllerBase
{
    [HttpGet("trip-configurations/{configurationId:long}/rates")]
    [RequirePermission(PermissionCodes.TRP_RATE_VIEW)]
    public async Task<IActionResult> List(long configurationId, [FromQuery] bool includeInactive = false) =>
        Ok(ApiResponse<IReadOnlyList<TripRateModel>>.Ok(await rates.ListAsync(configurationId, includeInactive)));

    [HttpPost("trip-configurations/{configurationId:long}/rates")]
    [RequirePermission(PermissionCodes.TRP_RATE_CONFIGURE)]
    public async Task<IActionResult> Create(long configurationId, [FromBody] SaveTripRateRequest request) =>
        Ok(ApiResponse<TripRateModel>.Ok(await rates.CreateAsync(configurationId, request)));

    [HttpPut("trip-rates/{rateId:long}")]
    [RequirePermission(PermissionCodes.TRP_RATE_CONFIGURE)]
    public async Task<IActionResult> Update(long rateId, [FromBody] UpdateTripRateRequest request) =>
        Ok(ApiResponse<TripRateModel>.Ok(await rates.UpdateAsync(rateId, request)));

    [HttpPost("trip-rates/{rateId:long}/inactivate")]
    [RequirePermission(PermissionCodes.TRP_RATE_CONFIGURE)]
    public async Task<IActionResult> Inactivate(long rateId, [FromBody] InactivateTripRateRequest request)
    {
        await rates.InactivateAsync(rateId, request);
        return Ok(ApiResponse.Ok());
    }

    [HttpPost("trip-configurations/{configurationId:long}/rates/split")]
    [RequirePermission(PermissionCodes.TRP_RATE_CONFIGURE)]
    public async Task<IActionResult> Split(long configurationId, [FromBody] SplitTripRateRequest request) =>
        Ok(ApiResponse<IReadOnlyList<TripRateModel>>.Ok(await rates.SplitAsync(configurationId, request)));

    [HttpGet("trip-rates/resolve")]
    [RequirePermission(PermissionCodes.TRP_RATE_VIEW)]
    public async Task<IActionResult> Resolve([FromQuery] int customerId, [FromQuery] long tripConfigurationId, [FromQuery] DateOnly date) =>
        Ok(ApiResponse<RateResolutionResult>.Ok(await rates.ResolveAsync(customerId, tripConfigurationId, date)));
}
