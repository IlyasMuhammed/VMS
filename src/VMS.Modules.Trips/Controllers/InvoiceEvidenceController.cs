using Microsoft.AspNetCore.Mvc;
using VMS.Modules.Trips.Models;
using VMS.Modules.Trips.Services;
using VMS.Shared.Authorization;
using VMS.Shared.Pagination;

namespace VMS.Modules.Trips.Controllers;

/// <summary>Invoice evidence (§41). "View/download — Admin, Finance, Read Only; re-render — Admin"; retry a
/// failed generation is Finance's own action, distinct from Admin's re-render.</summary>
[ApiController]
[Route("api/invoices/{invoiceId:long}/evidence")]
public sealed class InvoiceEvidenceController(IInvoiceEvidenceGenerationService evidence) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCodes.TRP_INVOICE_VIEW)]
    public async Task<IActionResult> List(long invoiceId) => Ok(ApiResponse<IReadOnlyList<InvoiceEvidenceModel>>.Ok(await evidence.ListAsync(invoiceId)));

    [HttpGet("{evidenceId:long}/download")]
    [RequirePermission(PermissionCodes.TRP_INVOICE_VIEW)]
    public async Task<IActionResult> Download(long invoiceId, long evidenceId) => Ok(ApiResponse<InvoiceEvidenceDownloadModel>.Ok(await evidence.DownloadAsync(evidenceId)));

    [HttpPost("{evidenceId:long}/retry")]
    [RequirePermission(PermissionCodes.TRP_INVOICE_EVIDENCE_RETRY)]
    public async Task<IActionResult> Retry(long invoiceId, long evidenceId) =>
        Ok(ApiResponse<InvoiceEvidenceModel>.Ok(await evidence.RetryAsync(evidenceId, User.GetUserId()), "Invoice evidence generation retried."));

    [HttpPost("rerender")]
    [RequirePermission(PermissionCodes.TRP_INVOICE_EVIDENCE_RERENDER)]
    public async Task<IActionResult> Rerender(long invoiceId) =>
        Ok(ApiResponse<InvoiceEvidenceModel>.Ok(await evidence.RerenderAsync(invoiceId, User.GetUserId()), "Invoice evidence re-rendered."));
}
