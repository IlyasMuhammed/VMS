using Microsoft.AspNetCore.Mvc;
using VMS.Modules.Vehicles.Models;
using VMS.Modules.Vehicles.Services;
using VMS.Shared.Authorization;
using VMS.Shared.Pagination;

namespace VMS.Modules.Vehicles.Controllers;

/// <summary>A vehicle's recurring charges and their generated entries (FSD §19A). Bank Installment is not configured here — see §19A.3.</summary>
[ApiController]
[Route("api/vehicles/{id:int}/recurring-charges")]
public class RecurringChargesController(IRecurringChargeService charges, IPayablesService payables) : ControllerBase
{
    private VehicleCaller Caller => new(
        User.GetUserId(),
        User.FindFirst("user_name")?.Value,
        User.IsSuperAdmin(),
        User.Claims.Where(c => c.Type == "permission").Select(c => c.Value).ToHashSet());

    [HttpGet]
    [RequirePermission(PermissionCodes.VEH_VIEW)]
    public async Task<IActionResult> List(int id) => Ok(ApiResponse<List<RecurringChargeModel>>.Ok(await charges.ListAsync(id)));

    [HttpPost]
    [RequirePermission(PermissionCodes.FIN_RECURRING_MANAGE)]
    public async Task<IActionResult> Create(int id, [FromBody] SaveRecurringChargeRequest request) =>
        Ok(ApiResponse<RecurringChargeModel>.Ok(await charges.CreateAsync(id, request, Caller), "Recurring charge added."));

    /// <summary>Amends the charge: the current row is end-dated and a new one takes its place from the effective date (BR-VH-033).</summary>
    [HttpPut("{chargeId:int}")]
    [RequirePermission(PermissionCodes.FIN_RECURRING_MANAGE)]
    public async Task<IActionResult> Amend(int id, int chargeId, [FromBody] SaveRecurringChargeRequest request) =>
        Ok(ApiResponse<RecurringChargeModel>.Ok(await charges.AmendAsync(id, chargeId, request, Caller), "Recurring charge updated."));

    [HttpPost("{chargeId:int}/end")]
    [RequirePermission(PermissionCodes.FIN_RECURRING_MANAGE)]
    public async Task<IActionResult> End(int id, int chargeId, [FromBody] EndRecurringChargeRequest request) =>
        Ok(ApiResponse<RecurringChargeModel>.Ok(await charges.EndAsync(id, chargeId, request, Caller), "Recurring charge ended."));

    /// <summary>This vehicle's Due and Overdue items — its recurring charge entries and its unpaid installments together (BR-VH-029), for the Due & Payments panel.</summary>
    [HttpGet("~/api/vehicles/{id:int}/payables")]
    [RequirePermission(PermissionCodes.VEH_VIEW)]
    public async Task<IActionResult> Payables(int id) => Ok(ApiResponse<List<PayableModel>>.Ok(await payables.ForVehicleAsync(id)));

    [HttpPost("entries/{entryId:int}/confirm")]
    [RequirePermission(PermissionCodes.FIN_DUE_CONFIRM)]
    public async Task<IActionResult> Confirm(int id, int entryId, [FromBody] ConfirmChargeEntryRequest request) =>
        Ok(ApiResponse<ChargeEntryPaymentModel>.Ok(await charges.ConfirmAsync(id, entryId, request, Caller), "Payment recorded."));

    [HttpPost("entries/{entryId:int}/waive")]
    [RequirePermission(PermissionCodes.FIN_DUE_WAIVE)]
    public async Task<IActionResult> Waive(int id, int entryId, [FromBody] WaiveChargeEntryRequest request)
    {
        await charges.WaiveAsync(id, entryId, request, Caller);
        return Ok(ApiResponse.Ok("Entry waived."));
    }

    [HttpPost("entries/{entryId:int}/cancel")]
    [RequirePermission(PermissionCodes.FIN_RECURRING_MANAGE)]
    public async Task<IActionResult> Cancel(int id, int entryId, [FromBody] WaiveChargeEntryRequest request)
    {
        await charges.CancelAsync(id, entryId, request, Caller);
        return Ok(ApiResponse.Ok("Entry cancelled."));
    }
}

/// <summary>The Payables Due workbench: fleet-wide Due and Overdue items across every vehicle and charge type (§19A.5, FR-VH-016).</summary>
[ApiController]
[Route("api/payables")]
public class PayablesController(IPayablesService payables) : ControllerBase
{
    private VehicleCaller Caller => new(
        User.GetUserId(),
        User.FindFirst("user_name")?.Value,
        User.IsSuperAdmin(),
        User.Claims.Where(c => c.Type == "permission").Select(c => c.Value).ToHashSet());

    [HttpGet]
    [RequirePermission(PermissionCodes.FIN_DUE_CONFIRM)]
    public async Task<IActionResult> List([FromQuery] PayablesQuery query) => Ok(ApiResponse<List<PayableModel>>.Ok(await payables.ListAsync(query)));

    /// <summary>Clears a batch in one action — twelve tracker fees at once — with a common payment date and mode. Each item is still checked and posted on its own, so one bad row does not stop the rest.</summary>
    [HttpPost("bulk-confirm")]
    [RequirePermission(PermissionCodes.FIN_DUE_CONFIRM)]
    public async Task<IActionResult> BulkConfirm([FromBody] BulkConfirmRequest request) =>
        Ok(ApiResponse<BulkConfirmResult>.Ok(await payables.BulkConfirmAsync(request, Caller)));

    /// <summary>The dashboard tile: due within 7 days and overdue, fleet-wide.</summary>
    [HttpGet("summary")]
    [RequirePermission(PermissionCodes.FIN_DUE_CONFIRM)]
    public async Task<IActionResult> Summary() => Ok(ApiResponse<PayablesSummaryModel>.Ok(await payables.SummaryAsync()));
}
