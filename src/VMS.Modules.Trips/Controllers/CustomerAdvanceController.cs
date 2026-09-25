using Microsoft.AspNetCore.Mvc;
using VMS.Modules.Trips.Models;
using VMS.Modules.Trips.Services;
using VMS.Shared.Authorization;
using VMS.Shared.Pagination;

namespace VMS.Modules.Trips.Controllers;

/// <summary>§37.4, §47.3: advance payments on Open trips. Admin, Finance (`TRP.PAYMENT.ADVANCE`).</summary>
[ApiController]
public sealed class CustomerAdvanceController(IAdvanceService advances) : ControllerBase
{
    [HttpPost("api/trips/{tripId:long}/advances")]
    [RequirePermission(PermissionCodes.TRP_PAYMENT_ADVANCE)]
    public async Task<IActionResult> Create(long tripId, [FromBody] CreateAdvanceRequest request) =>
        Ok(ApiResponse<CustomerAdvanceModel>.Ok(await advances.CreateAsync(tripId, request, User.GetUserId()), "Advance recorded."));

    [HttpGet("api/customer-advances")]
    [RequirePermission(PermissionCodes.TRP_PAYMENT_ADVANCE)]
    public async Task<IActionResult> List([FromQuery] int? customerId) =>
        Ok(ApiResponse<IReadOnlyList<CustomerAdvanceModel>>.Ok(await advances.ListAsync(customerId)));

    [HttpPost("api/customer-advances/{customerAdvanceId:long}/move")]
    [RequirePermission(PermissionCodes.TRP_PAYMENT_ADVANCE)]
    public async Task<IActionResult> Move(long customerAdvanceId, [FromBody] MoveAdvanceRequest request) =>
        Ok(ApiResponse<CustomerAdvanceModel>.Ok(await advances.MoveAsync(customerAdvanceId, request, User.GetUserId()), "Advance moved."));

    [HttpPost("api/customer-advances/{customerAdvanceId:long}/refund")]
    [RequirePermission(PermissionCodes.TRP_PAYMENT_ADVANCE)]
    public async Task<IActionResult> Refund(long customerAdvanceId, [FromBody] RefundAdvanceRequest request) =>
        Ok(ApiResponse<CustomerAdvanceModel>.Ok(await advances.RefundAsync(customerAdvanceId, request, User.GetUserId()), "Advance refunded."));

    [HttpPost("api/customer-advances/{customerAdvanceId:long}/reverse")]
    [RequirePermission(PermissionCodes.TRP_PAYMENT_ADVANCE)]
    public async Task<IActionResult> Reverse(long customerAdvanceId, [FromBody] ReverseAdvanceRequest request) =>
        Ok(ApiResponse<CustomerAdvanceModel>.Ok(await advances.ReverseAsync(customerAdvanceId, request, User.GetUserId()), "Advance reversed."));
}
