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

    /// <summary>The signed-in user's tenant logo for one mode (<c>light</c> or <c>dark</c>). 404 when none is uploaded.</summary>
    [HttpGet("logos/{variant}")]
    public async Task<IActionResult> GetLogo(string variant)
    {
        var logo = await tenants.GetLogoAsync(User.GetTenantId(), variant);
        return logo is null ? NotFound(ApiResponse.Fail("No logo uploaded.")) : this.LogoResult(logo);
    }
}
