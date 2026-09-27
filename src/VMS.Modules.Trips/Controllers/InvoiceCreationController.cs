using Microsoft.AspNetCore.Mvc;
using VMS.Modules.Trips.Models;
using VMS.Modules.Trips.Services;
using VMS.Shared.Authorization;
using VMS.Shared.Pagination;

namespace VMS.Modules.Trips.Controllers;

/// <summary>Invoice creation (§32.2–32.4, §33, §35, §52). First-time generation only.</summary>
[ApiController]
[Route("api/invoices")]
public sealed class InvoiceCreationController(IInvoiceCreationService creation, IInvoiceSearchService invoiceSearch) : ControllerBase
{
    /// <summary>The Invoice List (§48.5) — see <see cref="InvoiceListItem"/>'s own doc comment for why this
    /// exists despite not being one of the FSD's own numbered screens.</summary>
    [HttpGet("search")]
    [RequirePermission(PermissionCodes.TRP_INVOICE_VIEW)]
    public async Task<IActionResult> Search(
        [FromQuery] int? customerId, [FromQuery] string? status, [FromQuery] string? paymentStatus, [FromQuery] DateOnly? fromDate, [FromQuery] DateOnly? toDate,
        [FromQuery] bool? isActive, [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 25) =>
        Ok(ApiResponse<PaginatedResponse<InvoiceListItem>>.Ok(await invoiceSearch.SearchAsync(
            new InvoiceSearchFilter { CustomerId = customerId, Status = status, PaymentStatus = paymentStatus, FromDate = fromDate, ToDate = toDate, IsActive = isActive, Search = search },
            page, pageSize)));

    [HttpPost]
    [RequirePermission(PermissionCodes.TRP_INVOICE_GENERATE)]
    public async Task<IActionResult> Create([FromBody] CreateInvoiceRequest request) =>
        Ok(ApiResponse<InvoiceModel>.Ok(await creation.CreateAsync(request, User.GetUserId()), "Invoice created."));

    [HttpGet("{invoiceId:long}")]
    [RequirePermission(PermissionCodes.TRP_INVOICE_VIEW)]
    public async Task<IActionResult> Get(long invoiceId) => Ok(ApiResponse<InvoiceModel>.Ok(await creation.GetAsync(invoiceId)));

    /// <summary>§48.1: "History button on every record."</summary>
    [HttpGet("{invoiceId:long}/history")]
    [RequirePermission(PermissionCodes.TRP_INVOICE_VIEW)]
    public async Task<IActionResult> History(long invoiceId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50) =>
        Ok(ApiResponse<InvoiceAuditHistory>.Ok(await creation.HistoryAsync(invoiceId, page, pageSize, User)));
}
