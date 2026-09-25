using Microsoft.AspNetCore.Mvc;
using VMS.Modules.Trips.Models;
using VMS.Modules.Trips.Services;
using VMS.Shared.Authorization;
using VMS.Shared.Pagination;

namespace VMS.Modules.Trips.Controllers;

/// <summary>Customer tax/deduction rules (FSD §14). Edit — <see cref="PermissionCodes.TRP_TAXRULE_EDIT"/> (Admin, Finance).</summary>
[ApiController]
[Route("api")]
public sealed class CustomerTaxRuleController(ICustomerTaxRuleService taxRules) : ControllerBase
{
    [HttpGet("customers/{customerId:int}/tax-rules")]
    [RequirePermission(PermissionCodes.TRP_CUSTOMER_VIEW)]
    public async Task<IActionResult> List(int customerId, [FromQuery] bool includeInactive = false) =>
        Ok(ApiResponse<IReadOnlyList<CustomerTaxRuleModel>>.Ok(await taxRules.ListAsync(customerId, includeInactive)));

    [HttpGet("customers/{customerId:int}/tax-rules/applicable")]
    [RequirePermission(PermissionCodes.TRP_CUSTOMER_VIEW)]
    public async Task<IActionResult> Applicable(int customerId, [FromQuery] DateOnly invoiceDate) =>
        Ok(ApiResponse<IReadOnlyList<CustomerTaxRuleModel>>.Ok(await taxRules.ResolveApplicableAsync(customerId, invoiceDate)));

    [HttpPost("customers/{customerId:int}/tax-rules")]
    [RequirePermission(PermissionCodes.TRP_TAXRULE_EDIT)]
    public async Task<IActionResult> Create(int customerId, [FromBody] SaveCustomerTaxRuleRequest request) =>
        Ok(ApiResponse<CustomerTaxRuleModel>.Ok(await taxRules.CreateAsync(customerId, request)));

    [HttpPut("customer-tax-rules/{ruleId:long}")]
    [RequirePermission(PermissionCodes.TRP_TAXRULE_EDIT)]
    public async Task<IActionResult> Update(long ruleId, [FromBody] UpdateCustomerTaxRuleRequest request) =>
        Ok(ApiResponse<CustomerTaxRuleModel>.Ok(await taxRules.UpdateAsync(ruleId, request)));

    [HttpPost("customer-tax-rules/{ruleId:long}/replace")]
    [RequirePermission(PermissionCodes.TRP_TAXRULE_EDIT)]
    public async Task<IActionResult> Replace(long ruleId, [FromBody] SaveCustomerTaxRuleRequest request) =>
        Ok(ApiResponse<CustomerTaxRuleModel>.Ok(await taxRules.ReplaceAsync(ruleId, request)));
}
