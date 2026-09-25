using Microsoft.AspNetCore.Mvc;
using VMS.Modules.Trips.Models;
using VMS.Modules.Trips.Services;
using VMS.Shared.Authorization;
using VMS.Shared.Pagination;

namespace VMS.Modules.Trips.Controllers;

/// <summary>Trip lifecycle transitions (FSD §24, CC-15). Every action here needs either the back-office
/// <c>TRP.TRIP.STATUS</c> permission or, for the normal day-to-day moves, to be the trip's own assigned driver —
/// see <see cref="TripLifecycleService.RequireStatusPermission"/> — which is why these are not flat
/// <c>[RequirePermission]</c> actions like most of this module's controllers.</summary>
[ApiController]
[Route("api/trips/{tripId:long}")]
public sealed class TripLifecycleController(ITripLifecycleService lifecycle) : ControllerBase
{
    private TripCaller Caller => new(
        User.GetUserId(),
        User.FindFirst("user_name")?.Value,
        User.IsSuperAdmin(),
        User.Claims.Where(c => c.Type == "permission").Select(c => c.Value).ToHashSet());

    [HttpPost("status/{toStatus}")]
    [AuthenticatedOnly] // conditional: TripLifecycleService itself requires TRP.TRIP.STATUS or the trip's own driver.
    public async Task<IActionResult> Transition(long tripId, string toStatus, [FromBody] TransitionTripRequest request) =>
        Ok(ApiResponse<TripModel>.Ok(await lifecycle.TransitionAsync(tripId, toStatus, request, Caller)));

    [HttpPost("hold")]
    [AuthenticatedOnly]
    public async Task<IActionResult> Hold(long tripId, [FromBody] HoldTripRequest request) =>
        Ok(ApiResponse<TripModel>.Ok(await lifecycle.HoldAsync(tripId, request, Caller)));

    [HttpPost("resume")]
    [AuthenticatedOnly]
    public async Task<IActionResult> Resume(long tripId, [FromBody] ResumeTripRequest request) =>
        Ok(ApiResponse<TripModel>.Ok(await lifecycle.ResumeAsync(tripId, request, Caller)));

    [HttpPost("cancel")]
    [AuthenticatedOnly] // conditional: early cancels use the same back-office-or-own-driver rule, late ones need TRP.TRIP.STATUS outright.
    public async Task<IActionResult> Cancel(long tripId, [FromBody] CancelTripRequest request) =>
        Ok(ApiResponse<TripModel>.Ok(await lifecycle.CancelAsync(tripId, request, Caller)));

    /// <summary>Not driver-eligible (§24: needs Admin/Fleet Manager) — a flat permission gate is correct here.</summary>
    [HttpPost("reopen")]
    [RequirePermission(PermissionCodes.TRP_TRIP_REOPEN)]
    public async Task<IActionResult> Reopen(long tripId, [FromBody] ReopenTripRequest request) =>
        Ok(ApiResponse<TripModel>.Ok(await lifecycle.ReopenAsync(tripId, request, Caller)));

    [HttpPost("inactivate")]
    [RequirePermission(PermissionCodes.TRP_TRIP_INACTIVATE)]
    public async Task<IActionResult> Inactivate(long tripId, [FromBody] ChangeTripActiveRequest request) =>
        Ok(ApiResponse<TripModel>.Ok(await lifecycle.InactivateAsync(tripId, request)));

    [HttpPost("reactivate")]
    [RequirePermission(PermissionCodes.TRP_TRIP_INACTIVATE)]
    public async Task<IActionResult> Reactivate(long tripId, [FromBody] ChangeTripActiveRequest request) =>
        Ok(ApiResponse<TripModel>.Ok(await lifecycle.ReactivateAsync(tripId, request)));
}
