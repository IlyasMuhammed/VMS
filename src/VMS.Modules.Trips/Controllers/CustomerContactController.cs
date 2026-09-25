using Microsoft.AspNetCore.Mvc;
using VMS.Modules.Trips.Models;
using VMS.Modules.Trips.Services;
using VMS.Shared.Authorization;
using VMS.Shared.Pagination;

namespace VMS.Modules.Trips.Controllers;

/// <summary>Customer contacts (FSD §11). Edit — Admin, Finance (§44); view — every back-office role that can see the customer.</summary>
[ApiController]
[Route("api")]
public sealed class CustomerContactController(ICustomerContactService contacts) : ControllerBase
{
    [HttpGet("customers/{customerId:int}/contacts")]
    [RequirePermission(PermissionCodes.TRP_CUSTOMER_VIEW)]
    public async Task<IActionResult> List(int customerId, [FromQuery] bool includeInactive = false) =>
        Ok(ApiResponse<IReadOnlyList<CustomerContactModel>>.Ok(await contacts.ListAsync(customerId, includeInactive)));

    [HttpPost("customers/{customerId:int}/contacts")]
    [RequirePermission(PermissionCodes.TRP_CUSTOMER_EDIT)]
    public async Task<IActionResult> Create(int customerId, [FromBody] SaveCustomerContactRequest request) =>
        Ok(ApiResponse<CustomerContactSaveResult>.Ok(await contacts.CreateAsync(customerId, request)));

    [HttpPut("customer-contacts/{contactId:long}")]
    [RequirePermission(PermissionCodes.TRP_CUSTOMER_EDIT)]
    public async Task<IActionResult> Update(long contactId, [FromBody] SaveCustomerContactRequest request) =>
        Ok(ApiResponse<CustomerContactSaveResult>.Ok(await contacts.UpdateAsync(contactId, request)));

    [HttpPost("customer-contacts/{contactId:long}/deactivate")]
    [RequirePermission(PermissionCodes.TRP_CUSTOMER_EDIT)]
    public async Task<IActionResult> Deactivate(long contactId) =>
        Ok(ApiResponse<CustomerContactSaveResult>.Ok(await contacts.SetStatusAsync(contactId, active: false)));

    [HttpPost("customer-contacts/{contactId:long}/activate")]
    [RequirePermission(PermissionCodes.TRP_CUSTOMER_EDIT)]
    public async Task<IActionResult> Activate(long contactId) =>
        Ok(ApiResponse<CustomerContactSaveResult>.Ok(await contacts.SetStatusAsync(contactId, active: true)));
}
