using Microsoft.AspNetCore.Mvc;
using VMS.Modules.Trips.Models;
using VMS.Modules.Trips.Services;
using VMS.Shared.Authorization;
using VMS.Shared.Pagination;

namespace VMS.Modules.Trips.Controllers;

/// <summary>Trip income with a billable flag (§30). Not part of the driver-app channel table — every action is
/// back-office only, so this is a flat <c>[RequirePermission]</c> controller throughout.</summary>
[ApiController]
[Route("api/trips/{tripId:long}/income")]
public sealed class TripIncomeController(ITripIncomeService income) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCodes.TRP_INCOME_EDIT)]
    public async Task<IActionResult> List(long tripId) => Ok(ApiResponse<IReadOnlyList<TripIncomeModel>>.Ok(await income.ListAsync(tripId)));

    [HttpPost]
    [RequirePermission(PermissionCodes.TRP_INCOME_EDIT)]
    public async Task<IActionResult> Create(long tripId, [FromBody] CreateTripIncomeRequest request) =>
        Ok(ApiResponse<TripIncomeModel>.Ok(await income.CreateAsync(tripId, request), "Income recorded."));
}

[ApiController]
[Route("api/trip-income")]
public sealed class TripIncomeActionsController(ITripIncomeService income) : ControllerBase
{
    /// <summary>Not a literal endpoint in §47.2's own list, added on the same reasoning as Fuel/Expense's own
    /// void action — a trip income row is never physically deleted either.</summary>
    [HttpPost("{id:long}/void")]
    [RequirePermission(PermissionCodes.TRP_INCOME_EDIT)]
    public async Task<IActionResult> Void(long id, [FromBody] VoidTripIncomeRequest request) =>
        Ok(ApiResponse<TripIncomeModel>.Ok(await income.VoidAsync(id, request, User.GetUserId()), "Income voided."));

    /// <summary>"Billable income exposed for invoice lines" (§30/§47.2) — a small read endpoint so the not-yet-built
    /// invoicing task (CC-23+) has a real, already-tested place to read from, rather than an untested private
    /// method waiting for a caller.</summary>
    [HttpGet("billable")]
    [RequirePermission(PermissionCodes.TRP_INCOME_EDIT)]
    public async Task<IActionResult> Billable([FromQuery] int customerId) => Ok(ApiResponse<IReadOnlyList<TripIncomeModel>>.Ok(await income.ListBillableUnbilledAsync(customerId)));
}
