using Microsoft.EntityFrameworkCore;
using VMS.Modules.Auth.Data;
using VMS.Shared.Common;
using VMS.Shared.Users;

namespace VMS.Modules.Auth.Services;

/// <summary>Answers other modules' "who should be told" questions (Notifications) without letting them read the user or role tables.</summary>
internal sealed class UserDirectory(AuthDbContext db, ITenantContext tenantContext) : IUserDirectory
{
    public async Task<IReadOnlyList<UserInfo>> FindActiveByPermissionAsync(string permissionCode, CancellationToken cancellationToken = default)
    {
        var tenant = tenantContext.TenantId;

        // Every active role that carries the permission — global (platform) templates included; db.Roles' own query
        // filter already limits this to "global, or this tenant's own", the same as every other query in this module.
        var roleIds = await (
            from r in db.Roles.AsNoTracking()
            where r.IsActive
            join rp in db.RolePermissions.AsNoTracking() on r.RoleID equals rp.RoleID
            join p in db.Permissions.AsNoTracking() on rp.PermissionID equals p.PermissionID
            where p.Code == permissionCode
            select r.RoleID).ToListAsync(cancellationToken);
        if (roleIds.Count == 0) return [];

        var userIds = await db.UserRoles.AsNoTracking()
            .Where(ur => ur.TenantId == tenant && roleIds.Contains(ur.RoleID))
            .Select(ur => ur.UserID).Distinct().ToListAsync(cancellationToken);
        if (userIds.Count == 0) return [];

        return await db.UserAccounts.AsNoTracking()
            .Where(u => u.TenantId == tenant && u.IsActive && !u.IsDeleted && userIds.Contains(u.UserID))
            .Select(u => new UserInfo(u.UserID, (u.FirstName + " " + (u.LastName ?? "")).Trim(), u.Email))
            .ToListAsync(cancellationToken);
    }
}
