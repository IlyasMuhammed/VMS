using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using VMS.Modules.Trips.Models;
using VMS.Modules.Trips.Services;
using VMS.Shared.Authorization;
using VMS.Shared.Exceptions;
using VMS.Shared.Pagination;

namespace VMS.Modules.Trips.Controllers;

/// <summary>Proof of delivery (§23/§25). Upload is conditional (Trip.Documents or the trip's own assigned driver);
/// approve/reject is not driver-eligible at all, so those keep flat <c>[RequirePermission]</c> gates.</summary>
[ApiController]
[Route("api/trips/{tripId:long}/pod")]
public sealed class TripPODController(ITripPODService pods) : ControllerBase
{
    private TripCaller Caller => new(
        User.GetUserId(),
        User.FindFirst("user_name")?.Value,
        User.IsSuperAdmin(),
        User.Claims.Where(c => c.Type == "permission").Select(c => c.Value).ToHashSet());

    [HttpGet]
    [AuthenticatedOnly]
    public async Task<IActionResult> List(long tripId) => Ok(ApiResponse<IReadOnlyList<TripPODModel>>.Ok(await pods.ListAsync(tripId, Caller)));

    [HttpPost]
    [RequestSizeLimit(11 * 1024 * 1024)]
    [AuthenticatedOnly]
    public async Task<IActionResult> Upload(long tripId, IFormFile file)
    {
        if (file is null || file.Length == 0) throw new BadRequestException("Choose a file to upload.");
        await using var stream = file.OpenReadStream();
        return Ok(ApiResponse<TripPODModel>.Ok(await pods.UploadAsync(tripId, stream, file.FileName, Caller), "Proof of delivery uploaded."));
    }
}

[ApiController]
[Route("api/trip-pods")]
public sealed class TripPODActionsController(ITripPODService pods) : ControllerBase
{
    [HttpPost("{id:long}/approve")]
    [RequirePermission(PermissionCodes.TRP_POD_APPROVE)]
    public async Task<IActionResult> Approve(long id) => Ok(ApiResponse<TripPODModel>.Ok(await pods.ApproveAsync(id, User.GetUserId()), "Proof of delivery approved."));

    /// <summary>Not a literal endpoint in §47.2's own list, added alongside Approve so <c>PodStatuses.Rejected</c>
    /// (§23's own fourth status value) is actually reachable rather than a dead enum member.</summary>
    [HttpPost("{id:long}/reject")]
    [RequirePermission(PermissionCodes.TRP_POD_APPROVE)]
    public async Task<IActionResult> Reject(long id, [FromBody] RejectPodRequest request) =>
        Ok(ApiResponse<TripPODModel>.Ok(await pods.RejectAsync(id, request, User.GetUserId()), "Proof of delivery rejected."));

    [HttpPost("{id:long}/download-link")]
    [RequirePermission(PermissionCodes.TRP_TRIP_VIEW)]
    public async Task<IActionResult> DownloadLink(long id) => Ok(ApiResponse<TripDownloadLinkModel>.Ok(await pods.DownloadLinkAsync(id)));
}
