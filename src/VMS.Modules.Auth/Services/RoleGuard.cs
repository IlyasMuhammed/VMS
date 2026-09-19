using Microsoft.EntityFrameworkCore;
using VMS.Modules.Auth.Data;
using VMS.Modules.Auth.Domain;
using VMS.Modules.Auth.Models;
using VMS.Shared.Common;
using VMS.Shared.Exceptions;

namespace VMS.Modules.Auth.Services;

/// <summary>Privilege-escalation rules shared by user and role management.</summary>
internal static class RoleGuard
{
    /// <summary>
    /// A caller may only hand out what they hold: they cannot assign the Super Admin role, nor any
    /// role carrying a permission they do not have themselves. Without this, anyone who could assign
    /// roles could promote themselves or an accomplice to a stronger role.
    /// </summary>
    public static async Task EnsureCanGrantRoleAsync(AuthDbContext db, Role role, CallerContext caller)
    {
        if (caller.IsSuperAdmin) return;

        if (role.RoleCode == RoleCodes.SuperAdmin)
            throw new ForbiddenException("Only a Super Admin can assign the Super Admin role.");

        var roleCodes = await PermissionCodesAsync(db, role.RoleID);
        if (roleCodes.Any(c => !caller.Permissions.Contains(c)))
            throw new ForbiddenException("You cannot assign a role that carries permissions you do not hold yourself.");
    }

    public static Task<List<string>> PermissionCodesAsync(AuthDbContext db, int roleId) =>
        (from rp in db.RolePermissions.AsNoTracking()
         join p in db.Permissions.AsNoTracking() on rp.PermissionID equals p.PermissionID
         where rp.RoleID == roleId
         select p.Code).ToListAsync();

    /// <summary>Global roles belong to the platform: only a Super Admin edits them.</summary>
    public static void EnsureCanModify(Role role, CallerContext caller)
    {
        if (role.IsGlobal && !caller.IsSuperAdmin)
            throw new ForbiddenException("Platform roles can only be changed by a Super Admin.");
    }
}
