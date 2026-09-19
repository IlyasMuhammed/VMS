using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using VMS.Modules.Auth.Data;
using VMS.Modules.Auth.Domain;
using VMS.Modules.Auth.Models;
using VMS.Shared.Exceptions;

namespace VMS.Modules.Auth.Services;

internal sealed partial class RoleService(AuthDbContext db) : IRoleService
{
    [GeneratedRegex("^[A-Z][A-Z0-9_]{1,49}$")]
    private static partial Regex RoleCodePattern();

    public Task<List<RoleListItemModel>> ListAsync() =>
        ProjectToList(db.Roles.AsNoTracking())
            .OrderByDescending(r => r.IsGlobal).ThenBy(r => r.Name)
            .ToListAsync();

    public async Task<RoleDetailModel> GetAsync(int roleId)
    {
        var role = await ProjectToList(db.Roles.AsNoTracking().Where(r => r.RoleID == roleId)).FirstOrDefaultAsync()
            ?? throw new NotFoundException("Role not found.");

        var allowed = await db.RolePermissions.AsNoTracking()
            .Where(rp => rp.RoleID == roleId)
            .Select(rp => rp.PermissionID)
            .ToListAsync();
        var allowedSet = allowed.ToHashSet();

        return new RoleDetailModel
        {
            RoleId = role.RoleId,
            Name = role.Name,
            RoleCode = role.RoleCode,
            Description = role.Description,
            IsActive = role.IsActive,
            IsGlobal = role.IsGlobal,
            ActiveUserCount = role.ActiveUserCount,
            PermissionCount = role.PermissionCount,
            PermissionGroups = await BuildGroupsAsync(allowedSet)
        };
    }

    public Task<List<PermissionGroupModel>> GetPermissionCatalogAsync() => BuildGroupsAsync([]);

    public async Task<RoleDetailModel> CreateAsync(CreateRoleRequest request, CallerContext caller)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new BadRequestException("Role name is required.");
        var code = (request.RoleCode ?? string.Empty).Trim().ToUpperInvariant();
        if (!RoleCodePattern().IsMatch(code))
            throw new BadRequestException("Role code must be 2-50 characters: capital letters, digits and underscores, starting with a letter.");

        var name = request.Name.Trim();
        // The filter scopes these to what the caller can see: global roles plus their own tenant's.
        if (await db.Roles.AnyAsync(r => r.RoleCode == code))
            throw new ConflictException($"A role with code '{code}' already exists.");
        if (await db.Roles.AnyAsync(r => r.Name == name))
            throw new ConflictException("A role with this name already exists.");

        var isGlobal = caller.IsSuperAdmin && request.IsGlobal;
        var role = new Role
        {
            Name = name,
            RoleCode = code,
            Description = request.Description?.Trim(),
            IsActive = true,
            IsGlobal = isGlobal,
            TenantId = isGlobal ? null : caller.TenantId
        };
        db.Roles.Add(role);
        await db.SaveChangesAsync();

        return await GetAsync(role.RoleID);
    }

    public async Task UpdateAsync(int roleId, UpdateRoleRequest request, CallerContext caller)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new BadRequestException("Role name is required.");

        var role = await LoadAsync(roleId);
        RoleGuard.EnsureCanModify(role, caller);

        var name = request.Name.Trim();
        if (await db.Roles.AnyAsync(r => r.Name == name && r.RoleID != roleId))
            throw new ConflictException("A role with this name already exists.");

        if (role.IsActive && !request.IsActive)
            await EnsureNoActiveUsersAsync(roleId);

        role.Name = name;
        role.Description = request.Description?.Trim();
        role.IsActive = request.IsActive;
        await db.SaveChangesAsync();
    }

    public async Task ReplacePermissionsAsync(int roleId, ReplacePermissionsRequest request, CallerContext caller)
    {
        var role = await LoadAsync(roleId);
        RoleGuard.EnsureCanModify(role, caller);

        var wanted = request.AllowedPermissionIds.Distinct().ToList();
        var permissions = await db.Permissions.Where(p => wanted.Contains(p.PermissionID)).ToListAsync();
        if (permissions.Count != wanted.Count)
            throw new BadRequestException("One or more permissions do not exist.");

        var existing = await db.RolePermissions.Where(rp => rp.RoleID == roleId).ToListAsync();
        var already = existing.Select(rp => rp.PermissionID).ToHashSet();

        if (!caller.IsSuperAdmin)
        {
            // You can only hand out what you hold. A permission the role already carries may stay even
            // if you lack it, so editing the rest of a role is not blocked by it.
            var notHeld = permissions
                .Where(p => !already.Contains(p.PermissionID) && !caller.Permissions.Contains(p.Code))
                .Select(p => p.Code).ToList();
            if (notHeld.Count > 0)
                throw new ForbiddenException($"You cannot grant permissions you do not hold yourself: {string.Join(", ", notHeld)}.");
        }

        db.RolePermissions.RemoveRange(existing.Where(rp => !wanted.Contains(rp.PermissionID)));
        var have = existing.Select(rp => rp.PermissionID).ToHashSet();
        db.RolePermissions.AddRange(wanted.Where(id => !have.Contains(id))
            .Select(id => new RolePermission { RoleID = roleId, PermissionID = id }));
        await db.SaveChangesAsync();

        // Permissions ride inside access tokens; end the affected users' sessions so the change
        // takes effect at their next refresh.
        await db.UserSessions
            .Where(s => s.RevokedAt == null && db.UserAccounts.Any(u => u.UserID == s.UserID && u.RoleID == roleId))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTime.UtcNow));
    }

    public async Task DeactivateAsync(int roleId, CallerContext caller)
    {
        var role = await LoadAsync(roleId);
        RoleGuard.EnsureCanModify(role, caller);
        await EnsureNoActiveUsersAsync(roleId);

        role.IsActive = false;
        await db.SaveChangesAsync();
    }

    public async Task<List<RoleUserModel>> GetUsersAsync(int roleId)
    {
        await LoadAsync(roleId);
        return await db.UserAccounts.AsNoTracking()
            .Where(u => u.RoleID == roleId && !u.IsDeleted)
            .OrderBy(u => u.FirstName)
            .Select(u => new RoleUserModel
            {
                UserId = u.UserID,
                FirstName = u.FirstName,
                LastName = u.LastName,
                Email = u.Email,
                Department = u.Department,
                IsActive = u.IsActive
            })
            .ToListAsync();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<Role> LoadAsync(int roleId) =>
        await db.Roles.FirstOrDefaultAsync(r => r.RoleID == roleId)
            ?? throw new NotFoundException("Role not found.");

    private async Task EnsureNoActiveUsersAsync(int roleId)
    {
        var count = await db.UserAccounts.CountAsync(u => u.RoleID == roleId && u.IsActive && !u.IsDeleted);
        if (count > 0)
            throw new ConflictException($"{count} active user(s) still hold this role. Move them to another role first.");
    }

    private async Task<List<PermissionGroupModel>> BuildGroupsAsync(HashSet<int> allowed)
    {
        var permissions = await db.Permissions.AsNoTracking().OrderBy(p => p.Module).ThenBy(p => p.Name).ToListAsync();
        return permissions
            .GroupBy(p => p.Module)
            .Select(g => new PermissionGroupModel
            {
                Module = g.Key,
                Permissions = g.Select(p => new PermissionItemModel
                {
                    PermissionId = p.PermissionID,
                    Name = p.Name,
                    Code = p.Code,
                    Description = p.Description,
                    IsAllowed = allowed.Contains(p.PermissionID)
                }).ToList()
            })
            .ToList();
    }

    private IQueryable<RoleListItemModel> ProjectToList(IQueryable<Role> query) =>
        query.Select(r => new RoleListItemModel
        {
            RoleId = r.RoleID,
            Name = r.Name,
            RoleCode = r.RoleCode,
            Description = r.Description,
            IsActive = r.IsActive,
            IsGlobal = r.IsGlobal,
            ActiveUserCount = db.UserAccounts.Count(u => u.RoleID == r.RoleID && u.IsActive && !u.IsDeleted),
            PermissionCount = db.RolePermissions.Count(rp => rp.RoleID == r.RoleID)
        });
}
