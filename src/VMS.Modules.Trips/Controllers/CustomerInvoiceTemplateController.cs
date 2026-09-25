using Microsoft.AspNetCore.Mvc;
using VMS.Modules.Trips.Models;
using VMS.Modules.Trips.Services;
using VMS.Shared.Authorization;
using VMS.Shared.Pagination;

namespace VMS.Modules.Trips.Controllers;

/// <summary>Customer invoice templates (FSD §15). Edit — <see cref="PermissionCodes.TRP_TEMPLATE_EDIT"/> (Admin, Finance).</summary>
[ApiController]
[Route("api")]
public sealed class CustomerInvoiceTemplateController(ICustomerInvoiceTemplateService templates) : ControllerBase
{
    [HttpGet("customers/{customerId:int}/invoice-templates")]
    [RequirePermission(PermissionCodes.TRP_CUSTOMER_VIEW)]
    public async Task<IActionResult> List(int customerId, [FromQuery] bool includeInactive = false) =>
        Ok(ApiResponse<IReadOnlyList<CustomerInvoiceTemplateModel>>.Ok(await templates.ListAsync(customerId, includeInactive)));

    [HttpGet("customers/{customerId:int}/invoice-templates/applicable")]
    [RequirePermission(PermissionCodes.TRP_CUSTOMER_VIEW)]
    public async Task<IActionResult> Applicable(int customerId, [FromQuery] DateOnly invoiceDate) =>
        Ok(ApiResponse<ApplicableInvoiceTemplatesModel>.Ok(await templates.ResolveApplicableAsync(customerId, invoiceDate)));

    [HttpPost("customers/{customerId:int}/invoice-templates")]
    [RequirePermission(PermissionCodes.TRP_TEMPLATE_EDIT)]
    public async Task<IActionResult> Create(int customerId, [FromBody] CreateCustomerInvoiceTemplateRequest request) =>
        Ok(ApiResponse<CustomerInvoiceTemplateModel>.Ok(await templates.CreateAsync(customerId, request)));

    [HttpPost("customer-invoice-templates/{templateId:long}/new-version")]
    [RequirePermission(PermissionCodes.TRP_TEMPLATE_EDIT)]
    public async Task<IActionResult> NewVersion(long templateId, [FromBody] NewInvoiceTemplateVersionRequest request) =>
        Ok(ApiResponse<CustomerInvoiceTemplateModel>.Ok(await templates.NewVersionAsync(templateId, request)));

    [HttpPost("customer-invoice-templates/{templateId:long}/activate")]
    [RequirePermission(PermissionCodes.TRP_TEMPLATE_EDIT)]
    public async Task<IActionResult> Activate(long templateId, [FromBody] ActivateCustomerInvoiceTemplateRequest request) =>
        Ok(ApiResponse<CustomerInvoiceTemplateModel>.Ok(await templates.ActivateAsync(templateId, request)));

    [HttpPost("customer-invoice-templates/{templateId:long}/set-default")]
    [RequirePermission(PermissionCodes.TRP_TEMPLATE_EDIT)]
    public async Task<IActionResult> SetDefault(long templateId) =>
        Ok(ApiResponse<CustomerInvoiceTemplateModel>.Ok(await templates.SetDefaultAsync(templateId)));

    [HttpPost("customer-invoice-templates/{templateId:long}/deactivate")]
    [RequirePermission(PermissionCodes.TRP_TEMPLATE_EDIT)]
    public async Task<IActionResult> Deactivate(long templateId) =>
        Ok(ApiResponse<CustomerInvoiceTemplateModel>.Ok(await templates.DeactivateAsync(templateId)));
}
