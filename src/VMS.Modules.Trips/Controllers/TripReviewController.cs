using Microsoft.AspNetCore.Mvc;
using VMS.Modules.Trips.Models;
using VMS.Modules.Trips.Services;
using VMS.Shared.Authorization;
using VMS.Shared.Pagination;

namespace VMS.Modules.Trips.Controllers;

/// <summary>§43/§47.2: "GET /api/trips/pending-review · POST /api/trips/{id}/release · /reject" — Operations/
/// Fleet/Admin releasing or rejecting a trip a driver created in the app. One combined permission
/// (<c>TRP.TRIP.REVIEW</c>) for view and act, matching §44's own RBAC row ("Review / release driver-created
/// trips") — no separate view-only permission is named there.</summary>
[ApiController]
[Route("api/trips")]
public sealed class TripReviewController(ITripReviewService review) : ControllerBase
{
    [HttpGet("pending-review")]
    [RequirePermission(PermissionCodes.TRP_TRIP_REVIEW)]
    public async Task<IActionResult> PendingReview() => Ok(ApiResponse<IReadOnlyList<TripPendingReviewItem>>.Ok(await review.PendingReviewAsync()));

    [HttpPost("{tripId:long}/release")]
    [RequirePermission(PermissionCodes.TRP_TRIP_REVIEW)]
    public async Task<IActionResult> Release(long tripId, [FromBody] ReleaseTripRequest request) =>
        Ok(ApiResponse<TripModel>.Ok(await review.ReleaseAsync(tripId, request, User.GetUserId()), "Trip released."));

    [HttpPost("{tripId:long}/reject")]
    [RequirePermission(PermissionCodes.TRP_TRIP_REVIEW)]
    public async Task<IActionResult> Reject(long tripId, [FromBody] RejectTripRequest request) =>
        Ok(ApiResponse<TripModel>.Ok(await review.RejectAsync(tripId, request, User.GetUserId()), "Trip rejected."));
}
