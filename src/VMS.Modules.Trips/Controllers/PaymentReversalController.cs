using Microsoft.AspNetCore.Mvc;
using VMS.Modules.Trips.Models;
using VMS.Modules.Trips.Services;
using VMS.Shared.Authorization;
using VMS.Shared.Pagination;

namespace VMS.Modules.Trips.Controllers;

/// <summary>§37 BR-P5, AC-40: reverse a wrongly-recorded payment. Admin, Finance Lead (`TRP.PAYMENT.REVERSE`).</summary>
[ApiController]
[Route("api/invoice-payments")]
public sealed class PaymentReversalController(IPaymentReversalService reversals) : ControllerBase
{
    [HttpPost("{invoicePaymentId:long}/reverse")]
    [RequirePermission(PermissionCodes.TRP_PAYMENT_REVERSE)]
    public async Task<IActionResult> Reverse(long invoicePaymentId, [FromBody] ReversePaymentRequest request) =>
        Ok(ApiResponse<InvoicePaymentReversalModel>.Ok(await reversals.ReverseAsync(invoicePaymentId, request, User.GetUserId()), "Payment reversed."));
}
