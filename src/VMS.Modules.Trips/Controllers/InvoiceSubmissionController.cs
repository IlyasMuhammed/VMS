using Microsoft.AspNetCore.Mvc;
using VMS.Modules.Trips.Models;
using VMS.Modules.Trips.Services;
using VMS.Shared.Authorization;
using VMS.Shared.Pagination;

namespace VMS.Modules.Trips.Controllers;

/// <summary>Invoice submit and cancel, no approval step (§36, §47.3, AC-35, AC-55). Submit carries CC-01's own
/// <c>[Idempotent]</c> filter — §47.3's own literal body shape names "If-Match + Idempotency-Key" for this one
/// endpoint specifically, and this is that filter's first real production use since it was scaffolded. Cancel is
/// not decorated the same way: unlike Submit (which will post money once CC-30 lands), a retried Cancel is
/// already safe on its own — the status guard refuses a second one outright.</summary>
[ApiController]
[Route("api/invoices")]
public sealed class InvoiceSubmissionController(IInvoiceSubmissionService submissions) : ControllerBase
{
    [HttpPost("{invoiceId:long}/submit")]
    [RequirePermission(PermissionCodes.TRP_INVOICE_SUBMIT)]
    [Idempotent]
    public async Task<IActionResult> Submit(long invoiceId, [FromBody] SubmitInvoiceRequest request) =>
        Ok(ApiResponse<InvoiceModel>.Ok(await submissions.SubmitAsync(invoiceId, request, User.GetUserId()), "Invoice submitted."));

    [HttpPost("{invoiceId:long}/cancel")]
    [RequirePermission(PermissionCodes.TRP_INVOICE_CANCEL)]
    public async Task<IActionResult> Cancel(long invoiceId, [FromBody] CancelInvoiceRequest request) =>
        Ok(ApiResponse<InvoiceModel>.Ok(await submissions.CancelAsync(invoiceId, request, User.GetUserId()), "Invoice cancelled."));
}
