using Microsoft.AspNetCore.Mvc;
using VMS.Modules.Trips.Models;
using VMS.Modules.Trips.Services;
using VMS.Shared.Authorization;
using VMS.Shared.Pagination;

namespace VMS.Modules.Trips.Controllers;

/// <summary>Trip issues (§23). Reporting is conditional (Trip.Status or the trip's own assigned driver); resolving
/// is a back-office-only action, so it keeps a flat <c>[RequirePermission]</c> gate.</summary>
[ApiController]
[Route("api/trips/{tripId:long}/issues")]
public sealed class TripIssueController(ITripIssueService issues) : ControllerBase
{
    private TripCaller Caller => new(
        User.GetUserId(),
        User.FindFirst("user_name")?.Value,
        User.IsSuperAdmin(),
        User.Claims.Where(c => c.Type == "permission").Select(c => c.Value).ToHashSet());

    [HttpGet]
    [AuthenticatedOnly]
    public async Task<IActionResult> List(long tripId) => Ok(ApiResponse<IReadOnlyList<TripIssueModel>>.Ok(await issues.ListAsync(tripId, Caller)));

    [HttpPost]
    [AuthenticatedOnly]
    public async Task<IActionResult> Create(long tripId, [FromBody] CreateTripIssueRequest request) =>
        Ok(ApiResponse<TripIssueModel>.Ok(await issues.CreateAsync(tripId, request, Caller), "Issue reported."));
}

/// <summary>Not a literal endpoint in §47.2's own list, added so "resolved flag" (§23) is actually reachable.</summary>
[ApiController]
[Route("api/trip-issues")]
public sealed class TripIssueActionsController(ITripIssueService issues) : ControllerBase
{
    [HttpPost("{id:long}/resolve")]
    [RequirePermission(PermissionCodes.TRP_TRIP_STATUS)]
    public async Task<IActionResult> Resolve(long id, [FromBody] ResolveTripIssueRequest request) =>
        Ok(ApiResponse<TripIssueModel>.Ok(await issues.ResolveAsync(id, request, User.GetUserId()), "Issue resolved."));
}
