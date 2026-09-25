using Microsoft.AspNetCore.Mvc;
using VMS.Modules.Trips.Models;
using VMS.Modules.Trips.Services;
using VMS.Shared.Authorization;
using VMS.Shared.Pagination;

namespace VMS.Modules.Trips.Controllers;

/// <summary>§40, §47.2: "POST /api/invoices/{id}/carry-forward · /refund · ... → Credit handling →
/// Payment.CarryForward / Payment.Refund."</summary>
[ApiController]
[Route("api/invoices/{invoiceId:long}")]
public sealed class CustomerCreditController(ICustomerCreditService credit) : ControllerBase
{
    [HttpPost("carry-forward")]
    [RequirePermission(PermissionCodes.TRP_PAYMENT_CARRYFORWARD)]
    public async Task<IActionResult> CarryForward(long invoiceId, [FromBody] CarryForwardRequest request) =>
        Ok(ApiResponse<CarryForwardModel>.Ok(await credit.CarryForwardAsync(invoiceId, request, User.GetUserId()), "Credit carried forward."));

    [HttpPost("refund")]
    [RequirePermission(PermissionCodes.TRP_PAYMENT_REFUND)]
    public async Task<IActionResult> Refund(long invoiceId, [FromBody] RefundCreditRequest request) =>
        Ok(ApiResponse<CustomerRefundModel>.Ok(await credit.RefundAsync(invoiceId, request, User.GetUserId()), "Refund recorded."));
}
