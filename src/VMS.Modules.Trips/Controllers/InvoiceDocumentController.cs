using Microsoft.AspNetCore.Mvc;
using VMS.Modules.Trips.Models;
using VMS.Modules.Trips.Services;
using VMS.Shared.Authorization;
using VMS.Shared.Pagination;

namespace VMS.Modules.Trips.Controllers;

/// <summary>Invoice PDF rendering (§34). "Re-print returns the stored file; 'Re-render' is Admin-only."</summary>
[ApiController]
[Route("api/invoices/{invoiceId:long}/document")]
public sealed class InvoiceDocumentController(IInvoiceDocumentService documents) : ControllerBase
{
    [HttpPost("print")]
    [RequirePermission(PermissionCodes.TRP_INVOICE_VIEW)]
    public async Task<IActionResult> Print(long invoiceId) => Ok(ApiResponse<InvoiceDocumentDownloadModel>.Ok(await documents.PrintAsync(invoiceId, User.GetUserId())));

    [HttpPost("rerender")]
    [RequirePermission(PermissionCodes.TRP_INVOICE_RERENDER)]
    public async Task<IActionResult> Rerender(long invoiceId) =>
        Ok(ApiResponse<InvoiceDocumentDownloadModel>.Ok(await documents.RerenderAsync(invoiceId, User.GetUserId()), "Invoice document re-rendered."));
}
