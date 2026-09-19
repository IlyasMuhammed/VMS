using Microsoft.AspNetCore.Mvc;
using VMS.Modules.Tenancy.Models;
using VMS.Modules.Tenancy.Services;
using VMS.Shared.Authorization;
using VMS.Shared.Pagination;

namespace VMS.Modules.Tenancy.Controllers;

/// <summary>The signed-in user's own tenant — any authenticated user may read it.</summary>
[ApiController]
[Route("api/tenant")]
public class CurrentTenantController(ITenantService tenants) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var tenant = await tenants.GetTenantAsync(User.GetTenantId());
        return tenant is null
            ? NotFound(ApiResponse.Fail("Tenant not found."))
            : Ok(ApiResponse<TenantDetailModel>.Ok(tenant));
    }
}
