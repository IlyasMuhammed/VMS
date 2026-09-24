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
    // Which permissions each seeded role starts with (null = every permission). The five FLEET_MANAGER..READ_ONLY
    // rows are §23B.7's default role templates verbatim; TENANT_ADMIN above already is that table's "Administrator".
    private static readonly (string Code, string Name, string Description, string[]? Permissions)[] GlobalRoles =
    [
        (RoleCodes.SuperAdmin,  "Super Admin",  "Platform administrator. Manages tenants.",                     null),
        (RoleCodes.TenantAdmin, "Tenant Admin", "Full control of their own tenant's users and roles.",        null),
        (RoleCodes.Manager,     "Manager",      "Can view the tenant's users and roles.",                     [PermissionCodes.USER_VIEW, PermissionCodes.ROLE_VIEW]),
        (RoleCodes.Staff,       "Staff",        "Signs in; holds no user-management permissions.",            []),

        (RoleCodes.FleetManager, "Fleet Manager",
            "Inducts and manages the fleet day to day. No purchase price, finance amounts, profit or financial posting.",
            [
                PermissionCodes.BP_VIEW, PermissionCodes.BP_CREATE, PermissionCodes.BP_EDIT, PermissionCodes.BP_ROLE_MANAGE,
                PermissionCodes.VEH_VIEW, PermissionCodes.VEH_CREATE, PermissionCodes.VEH_EDIT, PermissionCodes.VEH_ACTIVATE,
                PermissionCodes.VEH_CATEGORY_CHANGE, PermissionCodes.VEH_STATUS_CHANGE, PermissionCodes.VEH_ITEM_MANAGE,
                PermissionCodes.VEH_DRIVER_ASSIGN, PermissionCodes.VEH_EXPORT, PermissionCodes.VEH_FIELD_FINANCE_VIEW,
                PermissionCodes.DOC_VIEW, PermissionCodes.DOC_UPLOAD, PermissionCodes.DOC_RENEW,
            ]),
        (RoleCodes.FinanceUser, "Finance User",
            "Owns acquisition, finance and every financial posting. Sees every cost, finance and profit field.",
            [
                PermissionCodes.BP_VIEW, PermissionCodes.BP_FIELD_SALARY_VIEW, PermissionCodes.BP_FIELD_CREDIT_VIEW, PermissionCodes.BP_FIELD_OPENING_VIEW,
                PermissionCodes.VEH_VIEW, PermissionCodes.VEH_ACQUISITION_EDIT, PermissionCodes.VEH_FIELD_COST_VIEW, PermissionCodes.VEH_FIELD_FINANCE_VIEW, PermissionCodes.VEH_FIELD_PROFIT_VIEW,
                PermissionCodes.FIN_EXPENSE_CREATE, PermissionCodes.FIN_EXPENSE_EDIT, PermissionCodes.FIN_EXPENSE_APPROVE, PermissionCodes.FIN_INCOME_CREATE, PermissionCodes.FIN_INCOME_EDIT,
                PermissionCodes.FIN_INSTALLMENT_PAY, PermissionCodes.FIN_AGREEMENT_MANAGE, PermissionCodes.FIN_RECURRING_MANAGE, PermissionCodes.FIN_RECURRING_AUTOPOST,
                PermissionCodes.FIN_DUE_CONFIRM, PermissionCodes.FIN_DUE_WAIVE, PermissionCodes.FIN_ADJUSTMENT_POST, PermissionCodes.FIN_PNL_VIEW, PermissionCodes.FIN_REPORT_VIEW,
                PermissionCodes.DOC_VIEW, PermissionCodes.DOC_UPLOAD,
            ]),
        (RoleCodes.OperationsUser, "Operations User",
            "Day to day dispatch: assigns drivers and records income. No cost, finance or profit fields.",
            [PermissionCodes.BP_VIEW, PermissionCodes.BP_CREATE, PermissionCodes.VEH_VIEW, PermissionCodes.VEH_DRIVER_ASSIGN, PermissionCodes.FIN_INCOME_CREATE, PermissionCodes.DOC_UPLOAD]),
        (RoleCodes.Driver, "Driver",
            "The driver app (Own vehicles scope, §23B.4): own profile only. No financial or partner access in this phase.",
            []),
        (RoleCodes.ReadOnly, "Read Only",
            "View and export partners, vehicles and documents. No cost, profit, salary or credit fields.",
            [PermissionCodes.BP_VIEW, PermissionCodes.BP_EXPORT, PermissionCodes.VEH_VIEW, PermissionCodes.VEH_EXPORT, PermissionCodes.DOC_VIEW, PermissionCodes.DOC_REGISTER_VIEW]),
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
                permission.Level = def.Level;
            }
            else
            {
                db.Permissions.Add(new Permission { Code = def.Code, Name = def.Name, Module = def.Module, Description = def.Description, Level = def.Level });
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
            if (existingCodes.Contains(code))
            {
                // A full-access role means "everything". When a release adds permissions it must keep
                // meaning that, or the administrators of an upgraded system would be locked out of the
                // new screens. Every other role is left alone: a new permission stays off for them until
                // an administrator grants it (BR-SEC-005).
                if (permissions is null) await GrantMissingPermissionsAsync(code, permissionIds);
                continue;
            }

            var role = new Role { RoleCode = code, Name = name, Description = description, IsGlobal = true, IsActive = true };
            db.Roles.Add(role);
            await db.SaveChangesAsync();

            var codes = permissions ?? PermissionCodes.Catalog.Select(d => d.Code).ToArray();
            db.RolePermissions.AddRange(codes.Select(c => new RolePermission { RoleID = role.RoleID, PermissionID = permissionIds[c] }));
            await db.SaveChangesAsync();
        }
    }

    private async Task GrantMissingPermissionsAsync(string roleCode, IReadOnlyDictionary<string, int> permissionIds)
    {
        var roleId = await db.Roles.IgnoreQueryFilters()
            .Where(r => r.IsGlobal && r.RoleCode == roleCode).Select(r => r.RoleID).FirstAsync();
        var held = (await db.RolePermissions.Where(rp => rp.RoleID == roleId).Select(rp => rp.PermissionID).ToListAsync()).ToHashSet();

        var missing = permissionIds.Values.Where(id => !held.Contains(id)).ToList();
        if (missing.Count == 0) return;

        db.RolePermissions.AddRange(missing.Select(id => new RolePermission { RoleID = roleId, PermissionID = id }));
        await db.SaveChangesAsync();
        logger.LogInformation("Granted {Count} new permission(s) to the {Role} role.", missing.Count, roleCode);
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
        user.UserRoles.Add(new UserRole { RoleID = superAdminRoleId, ScopeType = ScopeTypes.AllBranches, TenantId = user.TenantId });
        db.UserAccounts.Add(user);
        await db.SaveChangesAsync();

        await superAdmins.GrantAsync(user.UserID);
        logger.LogInformation("Seeded Super Admin {Email}.", user.Email);
    }
}
