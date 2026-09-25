using Microsoft.AspNetCore.Mvc;
using VMS.Modules.Trips.Models;
using VMS.Modules.Trips.Services;
using VMS.Shared.Authorization;
using VMS.Shared.Pagination;

namespace VMS.Modules.Trips.Controllers;

/// <summary>Customer billing addresses (FSD §12). Same permissions as contacts (§44: Admin, Finance edit).</summary>
[ApiController]
[Route("api")]
public sealed class CustomerBillingAddressController(ICustomerBillingAddressService addresses) : ControllerBase
{
    [HttpGet("customers/{customerId:int}/billing-addresses")]
    [RequirePermission(PermissionCodes.TRP_CUSTOMER_VIEW)]
    public async Task<IActionResult> List(int customerId, [FromQuery] bool includeInactive = false) =>
        Ok(ApiResponse<IReadOnlyList<CustomerBillingAddressModel>>.Ok(await addresses.ListAsync(customerId, includeInactive)));

    [HttpPost("customers/{customerId:int}/billing-addresses")]
    [RequirePermission(PermissionCodes.TRP_CUSTOMER_EDIT)]
    public async Task<IActionResult> Create(int customerId, [FromBody] SaveCustomerBillingAddressRequest request) =>
        Ok(ApiResponse<CustomerBillingAddressModel>.Ok(await addresses.CreateAsync(customerId, request)));

    [HttpPut("customer-billing-addresses/{addressId:long}")]
    [RequirePermission(PermissionCodes.TRP_CUSTOMER_EDIT)]
    public async Task<IActionResult> Update(long addressId, [FromBody] SaveCustomerBillingAddressRequest request) =>
        Ok(ApiResponse<CustomerBillingAddressModel>.Ok(await addresses.UpdateAsync(addressId, request)));

    [HttpPost("customer-billing-addresses/{addressId:long}/set-default")]
    [RequirePermission(PermissionCodes.TRP_CUSTOMER_EDIT)]
    public async Task<IActionResult> SetDefault(long addressId) =>
        Ok(ApiResponse<CustomerBillingAddressModel>.Ok(await addresses.SetDefaultAsync(addressId)));

    [HttpPost("customer-billing-addresses/{addressId:long}/deactivate")]
    [RequirePermission(PermissionCodes.TRP_CUSTOMER_EDIT)]
    public async Task<IActionResult> Deactivate(long addressId) =>
        Ok(ApiResponse<CustomerBillingAddressModel>.Ok(await addresses.SetStatusAsync(addressId, active: false)));

    [HttpPost("customer-billing-addresses/{addressId:long}/activate")]
    [RequirePermission(PermissionCodes.TRP_CUSTOMER_EDIT)]
    public async Task<IActionResult> Activate(long addressId) =>
        Ok(ApiResponse<CustomerBillingAddressModel>.Ok(await addresses.SetStatusAsync(addressId, active: true)));
}
