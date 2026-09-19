using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using VMS.Modules.Tenancy.Models;
using VMS.Modules.Tenancy.Services;
using VMS.Shared.Authorization;
using VMS.Shared.Exceptions;
using VMS.Shared.Pagination;

namespace VMS.Modules.Tenancy.Controllers;

/// <summary>Tenant administration — platform Super Admin only.</summary>
[ApiController]
[Route("api/system/tenants")]
[RequireSuperAdmin]
public class TenantsController(ITenantService tenants) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetList([FromQuery] TenantFilter filter) =>
        Ok(ApiResponse<PaginatedResponse<TenantListItemModel>>.Ok(await tenants.GetTenantsAsync(filter)));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var tenant = await tenants.GetTenantAsync(id);
        return tenant is null
            ? NotFound(ApiResponse.Fail("Tenant not found."))
            : Ok(ApiResponse<TenantDetailModel>.Ok(tenant));
    }

    /// <summary>Creates the tenant and its first admin together; the admin is invited to set a password.</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateTenantRequest request)
    {
        var result = await tenants.CreateTenantWithAdminAsync(request, User.GetUserId());
        return CreatedAtAction(nameof(GetById), new { id = result.TenantId },
            ApiResponse<CreateTenantResult>.Ok(result, "Tenant created."));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateTenantRequest request) =>
        await tenants.UpdateTenantAsync(id, request, User.GetUserId())
            ? Ok(ApiResponse.Ok("Tenant updated."))
            : NotFound(ApiResponse.Fail("Tenant not found."));

    /// <summary>Uploads the tenant's logo for one mode. <c>variant</c> is <c>light</c> or <c>dark</c>; PNG, JPEG or WebP, up to 512 KB.</summary>
    [HttpPut("{id:guid}/logos/{variant}")]
    // A hard ceiling for the request as a whole. It is deliberately above the 512 KB logo limit so an
    // oversized logo reaches the check below and gets a readable message instead of a framework error.
    [RequestSizeLimit(2 * 1024 * 1024)]
    public async Task<IActionResult> UploadLogo(Guid id, string variant, IFormFile file)
    {
        if (file.Length > TenantLogoRules.MaxBytes)
            throw new BadRequestException($"The logo must be {TenantLogoRules.MaxBytes / 1024} KB or smaller.");

        await using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer);
        return await tenants.SetLogoAsync(id, variant, buffer.ToArray(), User.GetUserId())
            ? Ok(ApiResponse.Ok("Logo saved."))
            : NotFound(ApiResponse.Fail("Tenant not found."));
    }

    [HttpGet("{id:guid}/logos/{variant}")]
    public async Task<IActionResult> GetLogo(Guid id, string variant)
    {
        var logo = await tenants.GetLogoAsync(id, variant);
        return logo is null ? NotFound(ApiResponse.Fail("No logo uploaded.")) : this.LogoResult(logo);
    }

    [HttpDelete("{id:guid}/logos/{variant}")]
    public async Task<IActionResult> DeleteLogo(Guid id, string variant) =>
        await tenants.DeleteLogoAsync(id, variant)
            ? Ok(ApiResponse.Ok("Logo removed."))
            : NotFound(ApiResponse.Fail("Tenant not found."));

    [HttpPatch("{id:guid}/status")]
    public async Task<IActionResult> SetStatus(Guid id, [FromBody] PatchTenantStatusRequest request) =>
        await tenants.SetStatusAsync(id, request.IsActive, User.GetUserId())
            ? Ok(ApiResponse.Ok(request.IsActive ? "Tenant activated." : "Tenant deactivated; all of its sessions were revoked."))
            : NotFound(ApiResponse.Fail("Tenant not found."));
}
