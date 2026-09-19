using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using VMS.Modules.Tenancy.Data;
using VMS.Modules.Tenancy.Domain;
using VMS.Modules.Tenancy.Models;
using VMS.Shared.Common;
using VMS.Shared.Exceptions;
using VMS.Shared.Pagination;

namespace VMS.Modules.Tenancy.Services;

internal sealed partial class TenantService(
    TenancyDbContext db,
    ITenantUserProvisioningService users,
    ITenantSnapshotProvider snapshots) : ITenantService
{
    [GeneratedRegex("^[A-Z0-9][A-Z0-9-]{1,29}$")]
    private static partial Regex TenantCodePattern();

    public async Task<PaginatedResponse<TenantListItemModel>> GetTenantsAsync(TenantFilter filter)
    {
        var page = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);

        var query = db.Tenants.AsNoTracking().AsQueryable();
        if (filter.IsActive is { } active)
            query = query.Where(t => t.IsActive == active);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(t => t.TenantName.Contains(term) || t.TenantCode.Contains(term));
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderBy(t => t.TenantName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new TenantListItemModel
            {
                Id = t.Id,
                TenantCode = t.TenantCode,
                TenantName = t.TenantName,
                IsActive = t.IsActive,
                ContactEmail = t.ContactEmail,
                CreatedDate = t.CreatedDate
            })
            .ToListAsync();

        return new PaginatedResponse<TenantListItemModel> { Items = items, TotalCount = total, Page = page, PageSize = pageSize };
    }

    public Task<TenantDetailModel?> GetTenantAsync(Guid id) =>
        db.Tenants.AsNoTracking()
            .Where(t => t.Id == id)
            .Select(t => new TenantDetailModel
            {
                Id = t.Id,
                TenantCode = t.TenantCode,
                TenantName = t.TenantName,
                IsActive = t.IsActive,
                ContactEmail = t.ContactEmail,
                ContactPhone = t.ContactPhone,
                Address = t.Address,
                Country = t.Country,
                TimeZone = t.TimeZone,
                CreatedDate = t.CreatedDate,
                ModifiedDate = t.ModifiedDate
            })
            .FirstOrDefaultAsync();

    public async Task<CreateTenantResult> CreateTenantWithAdminAsync(CreateTenantRequest request, int createdBy)
    {
        var code = request.TenantCode?.Trim().ToUpperInvariant() ?? string.Empty;
        if (!TenantCodePattern().IsMatch(code))
            throw new BadRequestException("Tenant code must be 2-30 characters: letters, digits and hyphens.");
        if (string.IsNullOrWhiteSpace(request.TenantName))
            throw new BadRequestException("Tenant name is required.");
        if (string.IsNullOrWhiteSpace(request.AdminFirstName) || string.IsNullOrWhiteSpace(request.AdminEmail))
            throw new BadRequestException("The tenant admin's first name and email are required.");
        if (await db.Tenants.AnyAsync(t => t.TenantCode == code))
            throw new ConflictException($"Tenant code '{code}' is already in use.");
        // Checked up front so a duplicate email fails before anything is written.
        if (await users.EmailExistsAsync(request.AdminEmail))
            throw new ConflictException("A user with the admin's email already exists.");

        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            TenantCode = code,
            TenantName = request.TenantName.Trim(),
            IsActive = true,
            ContactEmail = request.ContactEmail?.Trim(),
            ContactPhone = request.ContactPhone?.Trim(),
            Address = request.Address?.Trim(),
            Country = request.Country?.Trim(),
            TimeZone = request.TimeZone?.Trim(),
            CreatedBy = createdBy,
            CreatedDate = DateTime.UtcNow
        };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        try
        {
            var invite = await users.CreateTenantAdminAsync(
                tenant.Id, request.AdminFirstName.Trim(), request.AdminLastName?.Trim(), request.AdminEmail, createdBy);

            return new CreateTenantResult { TenantId = tenant.Id, AdminUserId = invite.UserId, AdminInviteLink = invite.InviteLink };
        }
        catch
        {
            // The tenant and its admin are one unit (the two live in different DbContexts, so
            // there is no shared transaction) — never leave a tenant behind that nobody can log in to.
            db.Tenants.Remove(tenant);
            await db.SaveChangesAsync();
            throw;
        }
    }

    public async Task<bool> UpdateTenantAsync(Guid id, UpdateTenantRequest request, int modifiedBy)
    {
        if (string.IsNullOrWhiteSpace(request.TenantName))
            throw new BadRequestException("Tenant name is required.");

        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == id);
        if (tenant is null) return false;

        tenant.TenantName = request.TenantName.Trim();
        tenant.ContactEmail = request.ContactEmail?.Trim();
        tenant.ContactPhone = request.ContactPhone?.Trim();
        tenant.Address = request.Address?.Trim();
        tenant.Country = request.Country?.Trim();
        tenant.TimeZone = request.TimeZone?.Trim();
        tenant.ModifiedBy = modifiedBy;
        tenant.ModifiedDate = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> SetStatusAsync(Guid id, bool isActive, int modifiedBy)
    {
        if (id == TenantDefaults.PlatformTenantId && !isActive)
            throw new BadRequestException("The platform tenant cannot be deactivated.");

        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == id);
        if (tenant is null) return false;

        tenant.IsActive = isActive;
        tenant.ModifiedBy = modifiedBy;
        tenant.ModifiedDate = DateTime.UtcNow;
        await db.SaveChangesAsync();

        snapshots.Invalidate(id);
        // Combined with the login-time tenant-active check, revoking sessions is what makes
        // deactivation immediate: refresh tokens stop working, and access tokens die at their expiry
        // (or sooner, via TenantMiddleware).
        if (!isActive)
            await users.RevokeSessionsForTenantAsync(id);

        return true;
    }
}
