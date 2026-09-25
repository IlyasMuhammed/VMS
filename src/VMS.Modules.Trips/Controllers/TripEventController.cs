using Microsoft.AspNetCore.Mvc;
using VMS.Modules.Trips.Models;
using VMS.Modules.Trips.Services;
using VMS.Shared.Authorization;
using VMS.Shared.Pagination;

namespace VMS.Modules.Trips.Controllers;

/// <summary>A trip's own timeline (§25). Conditional access (Trip.View/Trip.Status or the trip's own assigned
/// driver — §23's driver-app channel) is enforced inside <see cref="ITripEventService"/>, the same reason
/// <c>TripLifecycleController</c> uses <c>[AuthenticatedOnly]</c> rather than a flat permission gate.</summary>
[ApiController]
[Route("api/trips/{tripId:long}/events")]
public sealed class TripEventController(ITripEventService events) : ControllerBase
{
    private TripCaller Caller => new(
        User.GetUserId(),
        User.FindFirst("user_name")?.Value,
        User.IsSuperAdmin(),
        User.Claims.Where(c => c.Type == "permission").Select(c => c.Value).ToHashSet());

    [HttpGet]
    [AuthenticatedOnly]
    public async Task<IActionResult> List(long tripId) => Ok(ApiResponse<IReadOnlyList<TripEventModel>>.Ok(await events.ListAsync(tripId, Caller)));

    [HttpPost]
    [AuthenticatedOnly]
    public async Task<IActionResult> Create(long tripId, [FromBody] CreateTripEventRequest request) =>
        Ok(ApiResponse<TripEventModel>.Ok(await events.CreateManualAsync(tripId, request, Caller), "Event recorded."));
}
