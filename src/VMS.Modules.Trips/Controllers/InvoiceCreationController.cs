using Microsoft.AspNetCore.Mvc;
using VMS.Modules.Trips.Models;
using VMS.Modules.Trips.Services;
using VMS.Shared.Authorization;
using VMS.Shared.Pagination;

namespace VMS.Modules.Trips.Controllers;

/// <summary>Invoice creation (§32.2–32.4, §33, §35, §52). First-time generation only.</summary>
[ApiController]
[Route("api/invoices")]
public sealed class InvoiceCreationController(IInvoiceCreationService creation) : ControllerBase
{
    [HttpPost]
    [RequirePermission(PermissionCodes.TRP_INVOICE_GENERATE)]
    public async Task<IActionResult> Create([FromBody] CreateInvoiceRequest request) =>
        Ok(ApiResponse<InvoiceModel>.Ok(await creation.CreateAsync(request, User.GetUserId()), "Invoice created."));

    [HttpGet("{invoiceId:long}")]
    [RequirePermission(PermissionCodes.TRP_INVOICE_VIEW)]
    public async Task<IActionResult> Get(long invoiceId) => Ok(ApiResponse<InvoiceModel>.Ok(await creation.GetAsync(invoiceId)));
}
