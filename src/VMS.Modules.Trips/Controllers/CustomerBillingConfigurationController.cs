using Microsoft.AspNetCore.Mvc;
using VMS.Modules.Trips.Models;
using VMS.Modules.Trips.Services;
using VMS.Shared.Authorization;
using VMS.Shared.Pagination;

namespace VMS.Modules.Trips.Controllers;

/// <summary>Customer billing configuration (FSD §13). Admin, Finance only — no separate view-only role listed.</summary>
[ApiController]
[Route("api/customers/{customerId:int}/billing-configuration")]
public sealed class CustomerBillingConfigurationController(ICustomerBillingConfigurationService configurations) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCodes.TRP_CUSTOMER_VIEW)]
    public async Task<IActionResult> Get(int customerId, [FromQuery] DateOnly? asOf)
    {
        if (asOf is { } date)
        {
            var historical = await configurations.GetAsOfAsync(customerId, date);
            return historical is null ? NotFound(ApiResponse.Fail("No configuration was in force on that date.")) : Ok(ApiResponse<CustomerBillingConfigurationModel>.Ok(historical));
        }
        return Ok(ApiResponse<CustomerBillingConfigurationModel>.Ok(await configurations.GetCurrentAsync(customerId)));
    }

    [HttpPut]
    [RequirePermission(PermissionCodes.TRP_CUSTOMER_EDIT)]
    public async Task<IActionResult> Save(int customerId, [FromBody] SaveCustomerBillingConfigurationRequest request) =>
        Ok(ApiResponse<CustomerBillingConfigurationModel>.Ok(await configurations.SaveNewVersionAsync(customerId, request)));
}
