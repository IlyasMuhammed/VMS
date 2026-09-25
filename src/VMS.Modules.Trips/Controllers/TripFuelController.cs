using Microsoft.AspNetCore.Mvc;
using VMS.Modules.Trips.Models;
using VMS.Modules.Trips.Services;
using VMS.Shared.Authorization;
using VMS.Shared.Pagination;

namespace VMS.Modules.Trips.Controllers;

/// <summary>Trip fuel (§27, AC-23). Logging is conditional (Fuel.Edit or the trip's own assigned driver, per the
/// driver-app channel table); voiding is a back-office-only action.</summary>
[ApiController]
[Route("api/trips/{tripId:long}/fuel")]
public sealed class TripFuelController(ITripFuelService fuel) : ControllerBase
{
    private TripCaller Caller => new(
        User.GetUserId(),
        User.FindFirst("user_name")?.Value,
        User.IsSuperAdmin(),
        User.Claims.Where(c => c.Type == "permission").Select(c => c.Value).ToHashSet());

    [HttpGet]
    [AuthenticatedOnly]
    public async Task<IActionResult> List(long tripId) => Ok(ApiResponse<TripFuelListModel>.Ok(await fuel.ListAsync(tripId, Caller)));

    [HttpPost]
    [AuthenticatedOnly]
    public async Task<IActionResult> Create(long tripId, [FromBody] CreateTripFuelRequest request) =>
        Ok(ApiResponse<TripFuelModel>.Ok(await fuel.CreateAsync(tripId, request, Caller), "Fuel logged."));
}

[ApiController]
[Route("api/trip-fuel")]
public sealed class TripFuelActionsController(ITripFuelService fuel) : ControllerBase
{
    [HttpPost("{id:long}/void")]
    [RequirePermission(PermissionCodes.TRP_FUEL_EDIT)]
    public async Task<IActionResult> Void(long id, [FromBody] VoidTripFuelRequest request) =>
        Ok(ApiResponse<TripFuelModel>.Ok(await fuel.VoidAsync(id, request, User.GetUserId()), "Fuel entry voided."));
}
