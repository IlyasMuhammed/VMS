using Microsoft.AspNetCore.Mvc;
using VMS.Modules.Trips.Models;
using VMS.Modules.Trips.Services;
using VMS.Shared.Authorization;
using VMS.Shared.Pagination;

namespace VMS.Modules.Trips.Controllers;

/// <summary>The customer master (FSD §10). View — every back-office role; Create/Edit/Activate/Deactivate — Admin, Finance.</summary>
[ApiController]
[Route("api/customers")]
public sealed class CustomerController(ICustomerService customers) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCodes.TRP_CUSTOMER_VIEW)]
    public async Task<IActionResult> List([FromQuery] string? search, [FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 25) =>
        Ok(ApiResponse<PaginatedResponse<CustomerModel>>.Ok(await customers.ListAsync(search, status, page, pageSize)));

    /// <summary>Active customers only — for a picker on a screen that starts new work (a future trip, an invoice).</summary>
    [HttpGet("picker")]
    [RequirePermission(PermissionCodes.TRP_CUSTOMER_VIEW)]
    public async Task<IActionResult> Picker([FromQuery] string? search) =>
        Ok(ApiResponse<IReadOnlyList<CustomerPickerItem>>.Ok(await customers.PickerAsync(search)));

    [HttpGet("{customerId:int}")]
    [RequirePermission(PermissionCodes.TRP_CUSTOMER_VIEW)]
    public async Task<IActionResult> Get(int customerId) => Ok(ApiResponse<CustomerModel>.Ok(await customers.GetAsync(customerId)));

    [HttpPost]
    [RequirePermission(PermissionCodes.TRP_CUSTOMER_EDIT)]
    public async Task<IActionResult> Create([FromBody] SaveCustomerRequest request) =>
        Ok(ApiResponse<CustomerModel>.Ok(await customers.CreateAsync(request)));

    [HttpPut("{customerId:int}")]
    [RequirePermission(PermissionCodes.TRP_CUSTOMER_EDIT)]
    public async Task<IActionResult> Update(int customerId, [FromBody] SaveCustomerRequest request) =>
        Ok(ApiResponse<CustomerModel>.Ok(await customers.UpdateAsync(customerId, request)));

    /// <summary>§48.1: "History button on every record opens audit rows."</summary>
    [HttpGet("{customerId:int}/history")]
    [RequirePermission(PermissionCodes.TRP_CUSTOMER_VIEW)]
    public async Task<IActionResult> History(int customerId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50) =>
        Ok(ApiResponse<CustomerHistory>.Ok(await customers.HistoryAsync(customerId, page, pageSize, User)));

    [HttpGet("{customerId:int}/activation-check")]
    [RequirePermission(PermissionCodes.TRP_CUSTOMER_VIEW)]
    public async Task<IActionResult> ActivationCheck(int customerId) => Ok(ApiResponse<ActivationCheckModel>.Ok(await customers.ActivationCheckAsync(customerId)));

    [HttpPost("{customerId:int}/activate")]
    [RequirePermission(PermissionCodes.TRP_CUSTOMER_EDIT)]
    public async Task<IActionResult> Activate(int customerId, [FromBody] ChangeCustomerStatusRequest request) =>
        Ok(ApiResponse<CustomerModel>.Ok(await customers.ActivateAsync(customerId, request)));

    [HttpPost("{customerId:int}/deactivate")]
    [RequirePermission(PermissionCodes.TRP_CUSTOMER_EDIT)]
    public async Task<IActionResult> Deactivate(int customerId, [FromBody] ChangeCustomerStatusRequest request) =>
        Ok(ApiResponse<CustomerModel>.Ok(await customers.DeactivateAsync(customerId, request)));

    [HttpPost("{customerId:int}/reactivate")]
    [RequirePermission(PermissionCodes.TRP_CUSTOMER_EDIT)]
    public async Task<IActionResult> Reactivate(int customerId, [FromBody] ChangeCustomerStatusRequest request) =>
        Ok(ApiResponse<CustomerModel>.Ok(await customers.ReactivateAsync(customerId, request)));

    [HttpDelete("{customerId:int}")]
    [RequirePermission(PermissionCodes.TRP_CUSTOMER_EDIT)]
    public async Task<IActionResult> Delete(int customerId, [FromBody] ChangeCustomerStatusRequest request)
    {
        await customers.DeleteAsync(customerId, request);
        return Ok(ApiResponse.Ok());
    }
}
