using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using VMS.Modules.Trips.Models;
using VMS.Modules.Trips.Services;
using VMS.Shared.Authorization;
using VMS.Shared.Exceptions;
using VMS.Shared.Pagination;

namespace VMS.Modules.Trips.Controllers;

/// <summary>Trip documents that are not a POD (§23). Conditional access (Trip.View/Trip.Documents or the trip's
/// own assigned driver) is enforced inside <see cref="ITripDocumentService"/>.</summary>
[ApiController]
[Route("api/trips/{tripId:long}/documents")]
public sealed class TripDocumentController(ITripDocumentService documents) : ControllerBase
{
    private TripCaller Caller => new(
        User.GetUserId(),
        User.FindFirst("user_name")?.Value,
        User.IsSuperAdmin(),
        User.Claims.Where(c => c.Type == "permission").Select(c => c.Value).ToHashSet());

    [HttpGet]
    [AuthenticatedOnly]
    public async Task<IActionResult> List(long tripId) => Ok(ApiResponse<IReadOnlyList<TripDocumentModel>>.Ok(await documents.ListAsync(tripId, Caller)));

    [HttpPost]
    [RequestSizeLimit(11 * 1024 * 1024)]
    [AuthenticatedOnly]
    public async Task<IActionResult> Upload(long tripId, [FromForm] string documentType, IFormFile file)
    {
        if (file is null || file.Length == 0) throw new BadRequestException("Choose a file to upload.");
        await using var stream = file.OpenReadStream();
        return Ok(ApiResponse<TripDocumentModel>.Ok(await documents.UploadAsync(tripId, documentType, stream, file.FileName, Caller), "Document uploaded."));
    }
}

/// <summary>A trip document's download link — a separate route since it does not belong to one trip's own route
/// segment (mirrors <c>DocumentActionsController</c>'s own reasoning).</summary>
[ApiController]
[Route("api/trip-documents")]
public sealed class TripDocumentActionsController(ITripDocumentService documents) : ControllerBase
{
    [HttpPost("{id:long}/download-link")]
    [RequirePermission(PermissionCodes.TRP_TRIP_VIEW)]
    public async Task<IActionResult> DownloadLink(long id) => Ok(ApiResponse<TripDownloadLinkModel>.Ok(await documents.DownloadLinkAsync(id)));
}
