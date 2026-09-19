using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using VMS.Modules.Auth.Domain;
using VMS.Modules.Auth.Infrastructure;
using VMS.Shared.Authorization;
using VMS.Shared.Common;

namespace VMS.Modules.Auth.Data;

/// <summary>
/// Seeds the permission catalog, the platform's global roles and the first Super Admin. Idempotent —
/// every step checks before it writes, and existing roles' permissions are never overwritten, so an
/// admin's later edits survive a restart.
/// </summary>
internal sealed class AuthDataSeeder(
    AuthDbContext db,
    IPasswordHasher<UserAccount> hasher,
    ISuperAdminService superAdmins,
    IConfiguration configuration,
    ILogger<AuthDataSeeder> logger)
{
    // Which permissions each seeded role starts with (null = every permission).
    private static readonly (string Code, string Name, string Description, string[]? Permissions)[] GlobalRoles =
    [
        (RoleCodes.SuperAdmin,  "Super Admin",  "Platform administrator. Manages tenants.",                     null),
        (RoleCodes.TenantAdmin, "Tenant Admin", "Full control of their own tenant's users and roles.",        null),
        (RoleCodes.Manager,     "Manager",      "Can view the tenant's users and roles.",                     [PermissionCodes.USER_VIEW, PermissionCodes.ROLE_VIEW]),
        (RoleCodes.Staff,       "Staff",        "Signs in; holds no user-management permissions.",            []),
    ];

    public async Task SeedAsync()
    {
        await SeedPermissionsAsync();
        await SeedRolesAsync();
        await SeedSuperAdminAsync();
    }

    private async Task SeedPermissionsAsync()
    {
        var existing = await db.Permissions.ToDictionaryAsync(p => p.Code);
        foreach (var def in PermissionCodes.Catalog)
        {
            if (existing.TryGetValue(def.Code, out var permission))
            {
                permission.Name = def.Name;
                permission.Module = def.Module;
                permission.Description = def.Description;
            }
            else
            {
                db.Permissions.Add(new Permission { Code = def.Code, Name = def.Name, Module = def.Module, Description = def.Description });
            }
        }
        await db.SaveChangesAsync();
    }

    private async Task SeedRolesAsync()
    {
        var permissionIds = await db.Permissions.ToDictionaryAsync(p => p.Code, p => p.PermissionID);
        var existingCodes = (await db.Roles.IgnoreQueryFilters()
            .Where(r => r.IsGlobal).Select(r => r.RoleCode).ToListAsync()).ToHashSet();

        foreach (var (code, name, description, permissions) in GlobalRoles)
        {
            if (existingCodes.Contains(code)) continue;

            var role = new Role { RoleCode = code, Name = name, Description = description, IsGlobal = true, IsActive = true };
            db.Roles.Add(role);
            await db.SaveChangesAsync();

            var codes = permissions ?? PermissionCodes.Catalog.Select(d => d.Code).ToArray();
            db.RolePermissions.AddRange(codes.Select(c => new RolePermission { RoleID = role.RoleID, PermissionID = permissionIds[c] }));
            await db.SaveChangesAsync();
        }
    }

    private async Task SeedSuperAdminAsync()
    {
        var superAdminRoleId = await db.Roles.IgnoreQueryFilters()
            .Where(r => r.IsGlobal && r.RoleCode == RoleCodes.SuperAdmin)
            .Select(r => r.RoleID).FirstAsync();

        if (await db.UserAccounts.IgnoreQueryFilters().AnyAsync(u => u.RoleID == superAdminRoleId && !u.IsDeleted))
            return;

        var email = configuration["Seed:SuperAdmin:Email"];
        var password = configuration["Seed:SuperAdmin:Password"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning(
                "No Super Admin exists and Seed:SuperAdmin:Email / Seed:SuperAdmin:Password are not set — " +
                "nobody will be able to create tenants. Set them (user-secrets or environment) and restart.");
            return;
        }

        PasswordPolicy.Validate(password);

        var user = new UserAccount
        {
            FirstName = configuration["Seed:SuperAdmin:FirstName"] ?? "Super",
            LastName = configuration["Seed:SuperAdmin:LastName"] ?? "Admin",
            Email = email.Trim().ToLowerInvariant(),
            RoleID = superAdminRoleId,
            TenantId = TenantDefaults.PlatformTenantId,
            IsActive = true,
            CreatedDate = DateTime.UtcNow
        };
        user.PasswordHash = hasher.HashPassword(user, password);
        db.UserAccounts.Add(user);
        await db.SaveChangesAsync();

        await superAdmins.GrantAsync(user.UserID);
        logger.LogInformation("Seeded Super Admin {Email}.", user.Email);
    }
}
