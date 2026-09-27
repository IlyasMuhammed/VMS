using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using VMS.Modules.Trips.Models;
using VMS.Modules.Trips.Services;
using VMS.Shared.Authorization;
using VMS.Shared.Exceptions;
using VMS.Shared.Pagination;

namespace VMS.Modules.Trips.Controllers;

/// <summary>
/// The driver app's own backend (FSD §43, §47.2): "responses omit amounts, rates and customer financial data,"
/// and "any request for another driver's trip returns 404" (§44). Every action is <c>[AuthenticatedOnly]</c>,
/// not a flat permission gate — "driver scope" here means the caller's own <c>LinkedPartnerId</c> (already
/// guaranteed, at account-creation time, to be a partner holding the Driver role — see
/// <c>UserService.ResolveDriverLinkAsync</c>), not a permission code. The mobile client itself is still blocked
/// on Q5 (§58 item 17: "driver app technology... client will explain later") — this is the technology-agnostic
/// REST surface any client, once chosen, would call.
/// </summary>
[ApiController]
[Route("api/driver")]
[AuthenticatedOnly]
public sealed class DriverController(IDriverTripService driver, ICallerScope scope) : ControllerBase
{
    private int DriverId => scope.LinkedPartnerId ?? throw new ForbiddenException("This account is not linked to a driver profile.");

    private TripCaller Caller => new(
        User.GetUserId(),
        User.FindFirst("user_name")?.Value,
        User.IsSuperAdmin(),
        User.Claims.Where(c => c.Type == "permission").Select(c => c.Value).ToHashSet());

    [HttpGet("trip-options")]
    public async Task<IActionResult> TripOptions() => Ok(ApiResponse<DriverTripOptionsModel>.Ok(await driver.TripOptionsAsync(DriverId)));

    [HttpPost("trips")]
    public async Task<IActionResult> CreateFixed([FromBody] CreateDriverFixedTripRequest request) =>
        Ok(ApiResponse<DriverTripDetailModel>.Ok(await driver.CreateFixedAsync(request, DriverId, User.GetUserId()), "Trip created."));

    [HttpPost("trips/open")]
    public async Task<IActionResult> CreateOpen([FromBody] CreateDriverOpenTripRequest request) =>
        Ok(ApiResponse<DriverTripDetailModel>.Ok(await driver.CreateOpenAsync(request, DriverId, User.GetUserId()), "Trip created."));

    [HttpGet("trips")]
    public async Task<IActionResult> MyTrips() => Ok(ApiResponse<DriverTripsModel>.Ok(await driver.MyTripsAsync(DriverId)));

    [HttpGet("trips/{tripId:long}")]
    public async Task<IActionResult> Get(long tripId) => Ok(ApiResponse<DriverTripDetailModel>.Ok(await driver.GetAsync(tripId, DriverId)));

    [HttpPost("trips/{tripId:long}/steps")]
    public async Task<IActionResult> Step(long tripId, [FromBody] DriverStepRequest request) =>
        Ok(ApiResponse<DriverTripDetailModel>.Ok(await driver.StepAsync(tripId, request.ToStatus, request.ToTransitionRequest(), DriverId, Caller), "Trip updated."));

    [HttpPost("trips/{tripId:long}/fuel")]
    public async Task<IActionResult> AddFuel(long tripId, [FromBody] CreateTripFuelRequest request) =>
        Ok(ApiResponse<TripFuelModel>.Ok(await driver.AddFuelAsync(tripId, request, DriverId, Caller), "Fuel logged."));

    [HttpPost("trips/{tripId:long}/expenses")]
    public async Task<IActionResult> AddExpense(long tripId, [FromBody] CreateTripExpenseRequest request) =>
        Ok(ApiResponse<TripExpenseModel>.Ok(await driver.AddExpenseAsync(tripId, request, DriverId, Caller), "Expense logged."));

    [HttpPost("trips/{tripId:long}/issues")]
    public async Task<IActionResult> AddIssue(long tripId, [FromBody] CreateTripIssueRequest request) =>
        Ok(ApiResponse<TripIssueModel>.Ok(await driver.AddIssueAsync(tripId, request, DriverId, Caller), "Issue reported."));

    /// <summary>§43's own POD screen: "Camera (multi-page), gallery" — reuses the exact multipart shape
    /// <c>TripPODController</c> already established (a photo is not something an offline JSON queue can
    /// meaningfully replay the same way a status step or a fuel log can), just wrapped so a driver only ever
    /// reaches their own trip and never one they weren't assigned.</summary>
    [HttpPost("trips/{tripId:long}/pod")]
    [RequestSizeLimit(11 * 1024 * 1024)]
    public async Task<IActionResult> UploadPod(long tripId, IFormFile file, [FromServices] ITripPODService pods)
    {
        if (file is null || file.Length == 0) throw new BadRequestException("Choose a file to upload.");
        await driver.GetAsync(tripId, DriverId); // 404s before ever opening the stream if this is not the caller's own trip.
        await using var stream = file.OpenReadStream();
        return Ok(ApiResponse<TripPODModel>.Ok(await pods.UploadAsync(tripId, stream, file.FileName, Caller), "Proof of delivery uploaded."));
    }

    [HttpPost("sync")]
    public async Task<IActionResult> Sync([FromBody] DriverSyncRequest request) =>
        Ok(ApiResponse<DriverSyncResultModel>.Ok(await driver.SyncAsync(request, DriverId, Caller)));
}

/// <summary>The app's own "Next step" button: the target status travels as its own field (mirroring
/// <c>TripLifecycleController.Transition</c>'s route segment), everything else is the same
/// <see cref="TransitionTripRequest"/> the office endpoint takes.</summary>
public sealed class DriverStepRequest
{
    public string ToStatus { get; set; } = string.Empty;
    public decimal? StartOdometer { get; set; }
    public decimal? EndOdometer { get; set; }
    public string RowVersion { get; set; } = string.Empty;
    public Guid? ClientEventId { get; set; }

    internal TransitionTripRequest ToTransitionRequest() => new() { StartOdometer = StartOdometer, EndOdometer = EndOdometer, RowVersion = RowVersion, ClientEventId = ClientEventId };
}
