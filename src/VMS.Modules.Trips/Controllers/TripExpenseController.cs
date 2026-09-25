using Microsoft.AspNetCore.Mvc;
using VMS.Modules.Trips.Models;
using VMS.Modules.Trips.Services;
using VMS.Shared.Authorization;
using VMS.Shared.Pagination;

namespace VMS.Modules.Trips.Controllers;

/// <summary>Trip expenses with approval (§29, AC-24). Logging is conditional (Expense.Edit or the trip's own
/// assigned driver); approve/reject and void are back-office-only actions.</summary>
[ApiController]
[Route("api/trips/{tripId:long}/expenses")]
public sealed class TripExpenseController(ITripExpenseService expenses) : ControllerBase
{
    private TripCaller Caller => new(
        User.GetUserId(),
        User.FindFirst("user_name")?.Value,
        User.IsSuperAdmin(),
        User.Claims.Where(c => c.Type == "permission").Select(c => c.Value).ToHashSet());

    [HttpGet]
    [AuthenticatedOnly]
    public async Task<IActionResult> List(long tripId) => Ok(ApiResponse<IReadOnlyList<TripExpenseModel>>.Ok(await expenses.ListAsync(tripId, Caller)));

    [HttpPost]
    [AuthenticatedOnly]
    public async Task<IActionResult> Create(long tripId, [FromBody] CreateTripExpenseRequest request) =>
        Ok(ApiResponse<TripExpenseModel>.Ok(await expenses.CreateAsync(tripId, request, Caller), "Expense logged."));
}

[ApiController]
[Route("api/trip-expenses")]
public sealed class TripExpenseActionsController(ITripExpenseService expenses) : ControllerBase
{
    [HttpPost("{id:long}/approve")]
    [RequirePermission(PermissionCodes.TRP_EXPENSE_APPROVE)]
    public async Task<IActionResult> Decide(long id, [FromBody] DecideTripExpenseRequest request) =>
        Ok(ApiResponse<TripExpenseModel>.Ok(await expenses.DecideAsync(id, request, User.GetUserId()), request.Approved ? "Expense approved." : "Expense rejected."));

    [HttpPost("{id:long}/void")]
    [RequirePermission(PermissionCodes.TRP_EXPENSE_EDIT)]
    public async Task<IActionResult> Void(long id, [FromBody] VoidTripExpenseRequest request) =>
        Ok(ApiResponse<TripExpenseModel>.Ok(await expenses.VoidAsync(id, request, User.GetUserId()), "Expense voided."));
}
