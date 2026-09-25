using Microsoft.AspNetCore.Mvc;
using VMS.Modules.Trips.Models;
using VMS.Modules.Trips.Services;
using VMS.Shared.Authorization;
using VMS.Shared.Pagination;

namespace VMS.Modules.Trips.Controllers;

/// <summary>Trips (FSD §21/§22, CC-12/13: Fixed and Open trip creation). Create — Admin, Fleet, Operations
/// (Driver-from-the-app is a later task, §43); view — every back-office role.</summary>
[ApiController]
[Route("api/trips")]
public sealed class TripController(ITripService trips, ITripSearchService search) : ControllerBase
{
    /// <summary>The one conditional check in this module (§20's driver-override permission) that a flat
    /// <c>[RequirePermission]</c> on the action can't express — mirrors <c>VehiclesController</c>'s own <c>Caller</c>.</summary>
    private TripCaller Caller => new(
        User.GetUserId(),
        User.FindFirst("user_name")?.Value,
        User.IsSuperAdmin(),
        User.Claims.Where(c => c.Type == "permission").Select(c => c.Value).ToHashSet());

    /// <summary>The Trip List / Trip Desk board (§48.4) — built as part of CC-43's own back-office UI, since no
    /// earlier task ever needed to list trips at all (every one of them reads exactly one trip it already has
    /// the id for). Kept as a query-string filter bag rather than a POST body, matching every other list
    /// endpoint in this module (Customer, City, Route, …).</summary>
    [HttpGet("search")]
    [RequirePermission(PermissionCodes.TRP_TRIP_VIEW)]
    public async Task<IActionResult> Search(
        [FromQuery] DateOnly? fromDate, [FromQuery] DateOnly? toDate, [FromQuery] int? customerId, [FromQuery] int? vehicleId, [FromQuery] int? driverId,
        [FromQuery] string? status, [FromQuery] string? tripType, [FromQuery] bool? isActive, [FromQuery] bool? invoiced,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25) =>
        Ok(ApiResponse<PaginatedResponse<TripListItem>>.Ok(await search.SearchAsync(
            new TripSearchFilter { FromDate = fromDate, ToDate = toDate, CustomerId = customerId, VehicleId = vehicleId, DriverId = driverId, Status = status, TripType = tripType, IsActive = isActive, Invoiced = invoiced },
            page, pageSize)));

    [HttpGet("{tripId:long}")]
    [RequirePermission(PermissionCodes.TRP_TRIP_VIEW)]
    public async Task<IActionResult> Get(long tripId) => Ok(ApiResponse<TripModel>.Ok(await trips.GetAsync(tripId)));

    /// <summary>§48.1: "History button on every record" — the same audit-table read every other master's own
    /// History tab uses (Customer, Vehicle, Partner), added here for the same reason CC-43 added Customer's.</summary>
    [HttpGet("{tripId:long}/history")]
    [RequirePermission(PermissionCodes.TRP_TRIP_VIEW)]
    public async Task<IActionResult> History(long tripId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50) =>
        Ok(ApiResponse<TripHistory>.Ok(await trips.HistoryAsync(tripId, page, pageSize, User)));

    [HttpPost]
    [RequirePermission(PermissionCodes.TRP_TRIP_CREATE)]
    public async Task<IActionResult> CreateFixed([FromBody] CreateFixedTripRequest request) =>
        Ok(ApiResponse<TripModel>.Ok(await trips.CreateFixedTripAsync(request, Caller)));

    [HttpPost("open")]
    [RequirePermission(PermissionCodes.TRP_TRIP_CREATE)]
    public async Task<IActionResult> CreateOpen([FromBody] CreateOpenTripRequest request) =>
        Ok(ApiResponse<TripModel>.Ok(await trips.CreateOpenTripAsync(request, Caller)));
}
