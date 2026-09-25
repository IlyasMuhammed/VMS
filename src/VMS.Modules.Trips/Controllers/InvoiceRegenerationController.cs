using Microsoft.AspNetCore.Mvc;
using VMS.Modules.Trips.Models;
using VMS.Modules.Trips.Services;
using VMS.Shared.Authorization;
using VMS.Shared.Pagination;

namespace VMS.Modules.Trips.Controllers;

/// <summary>§38/§39: invoice regeneration and overlap replacement. No approval step — `Invoice.Regenerate` alone
/// is enough.</summary>
[ApiController]
[Route("api/invoices/{invoiceId:long}")]
public sealed class InvoiceRegenerationController(IInvoiceRegenerationService regeneration) : ControllerBase
{
    [HttpPost("regenerate")]
    [RequirePermission(PermissionCodes.TRP_INVOICE_REGENERATE)]
    public async Task<IActionResult> Regenerate(long invoiceId, [FromBody] RegenerateInvoiceRequest request) =>
        Ok(ApiResponse<InvoiceRegenerationModel>.Ok(await regeneration.RegenerateAsync(invoiceId, request, User.GetUserId()), "Invoice regenerated."));
}
