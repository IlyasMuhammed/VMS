using System.Net.Mail;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using VMS.Modules.Auth.Data;
using VMS.Modules.Auth.Domain;
using VMS.Modules.Auth.Infrastructure;
using VMS.Modules.Auth.Models;
using VMS.Shared.Authorization;
using VMS.Shared.Branches;
using VMS.Shared.Common;
using VMS.Shared.Exceptions;
using VMS.Shared.Pagination;
using VMS.Shared.Partners;

namespace VMS.Modules.Auth.Services;

internal sealed class UserService(
    AuthDbContext db,
    IPasswordHasher<UserAccount> hasher,
    ISuperAdminService superAdmins,
    IBranchDirectory branches,
    IPartnerDirectory partners,
    AuthNotifier notifier) : IUserService
{
    private static readonly TimeSpan InviteTtl = TimeSpan.FromHours(72);

    public async Task<UserDetailModel> CreateAsync(CreateUserRequest request, CallerContext caller)
    {
        if (string.IsNullOrWhiteSpace(request.FirstName))
            throw new BadRequestException("First name is required.");
        var email = NormalizeEmail(request.Email);

        var role = await db.Roles.FirstOrDefaultAsync(r => r.RoleID == request.RoleId && r.IsActive)
            ?? throw new BadRequestException("Choose an active role.");
        await RoleGuard.EnsureCanGrantRoleAsync(db, role, caller);

        var (scopeType, branchId) = await ResolveScopeAsync(request.ScopeType, request.BranchId);
        var linkedPartnerId = await ResolveDriverLinkAsync(request.LinkedPartnerId);

        // Email is unique across the whole platform (login takes only an email), so the check must
        // see other tenants' users too.
        if (await db.UserAccounts.IgnoreQueryFilters().AnyAsync(u => u.Email == email))
            throw new ConflictException("A user with this email already exists.");

        var rawToken = TokenHelper.NewToken();
        var user = new UserAccount
        {
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName?.Trim(),
            Email = email,
            Phone = request.Phone?.Trim(),
            Department = request.Department?.Trim(),
            RoleID = role.RoleID,
            LinkedPartnerId = linkedPartnerId,
            TenantId = caller.TenantId,
            // Inactive until the user follows the invite and sets their own password. The stored hash
            // is of a random value nobody knows, so the account cannot be logged into meanwhile.
            IsActive = false,
            CreatedBy = caller.UserId,
            CreatedDate = DateTime.UtcNow,
            InviteTokenHash = TokenHelper.Sha256Hex(rawToken),
            InviteTokenExpiresAt = DateTime.UtcNow.Add(InviteTtl)
        };
        user.PasswordHash = hasher.HashPassword(user, TokenHelper.NewToken());

        user.UserRoles.Add(new UserRole { RoleID = role.RoleID, ScopeType = scopeType, BranchId = branchId, TenantId = user.TenantId });
        db.UserAccounts.Add(user);
        await db.SaveChangesAsync();

        var link = notifier.InviteLink(rawToken);
        await notifier.SendInviteAsync(user, link, isReset: false);

        var detail = await GetAsync(user.UserID);
        detail.InviteLink = link;
        return detail;
    }

    public async Task<PaginatedResponse<UserListItemModel>> ListAsync(UserListFilter filter)
    {
        var page = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);

        var query = db.UserAccounts.AsNoTracking().Where(u => !u.IsDeleted);

        if (filter.RoleId is { } roleId) query = query.Where(u => u.RoleID == roleId);
        if (string.Equals(filter.Status, "active", StringComparison.OrdinalIgnoreCase)) query = query.Where(u => u.IsActive);
        if (string.Equals(filter.Status, "inactive", StringComparison.OrdinalIgnoreCase)) query = query.Where(u => !u.IsActive);
        if (!string.IsNullOrWhiteSpace(filter.Department)) query = query.Where(u => u.Department == filter.Department);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(u => u.FirstName.Contains(term) || (u.LastName != null && u.LastName.Contains(term)) || u.Email.Contains(term));
        }

        var total = await query.CountAsync();
        var items = await ProjectToList(query.OrderBy(u => u.FirstName).ThenBy(u => u.UserID).Skip((page - 1) * pageSize).Take(pageSize))
            .ToListAsync();

        var branchIds = items.Select(i => i.BranchId).Where(b => b.HasValue).Select(b => b!.Value).Distinct().ToList();
        if (branchIds.Count > 0)
        {
            var names = await branches.FindManyAsync(branchIds);
            foreach (var item in items) if (item.BranchId is { } b) item.BranchName = names.GetValueOrDefault(b)?.Name;
        }

        return new PaginatedResponse<UserListItemModel> { Items = items, TotalCount = total, Page = page, PageSize = pageSize };
    }

    public async Task<UserDetailModel> GetAsync(int userId)
    {
        var user = await db.UserAccounts.AsNoTracking()
            .Where(u => u.UserID == userId && !u.IsDeleted)
            .Select(u => new UserDetailModel
            {
                UserId = u.UserID,
                TenantId = u.TenantId,
                FirstName = u.FirstName,
                LastName = u.LastName,
                Email = u.Email,
                Phone = u.Phone,
                Department = u.Department,
                IsActive = u.IsActive,
                InvitePending = u.InviteTokenHash != null,
                CreatedDate = u.CreatedDate,
                LastLoginAt = u.LastLoginAt,
                LinkedPartnerId = u.LinkedPartnerId,
                Role = db.Roles
                    .Where(r => r.RoleID == u.RoleID)
                    .Select(r => new DropDownVM { ID = r.RoleID, Value = r.Name })
                    .FirstOrDefault()
            })
            .FirstOrDefaultAsync();
        if (user is null) throw new NotFoundException("User not found.");

        var roleRows = await (
            from ur in db.UserRoles.AsNoTracking()
            join r in db.Roles.AsNoTracking() on ur.RoleID equals r.RoleID
            where ur.UserID == userId
            select new { ur.RoleID, r.Name, ur.ScopeType, ur.BranchId }).ToListAsync();

        var branchIds = roleRows.Select(r => r.BranchId).Where(b => b.HasValue).Select(b => b!.Value).Distinct().ToList();
        var branchNames = branchIds.Count == 0 ? new Dictionary<Guid, string>() : (await branches.FindManyAsync(branchIds)).ToDictionary(kv => kv.Key, kv => kv.Value.Name);

        user.Roles = roleRows.Select(r => new UserRoleModel
        {
            RoleId = r.RoleID, RoleName = r.Name, IsPrimary = r.RoleID == user.Role?.ID,
            ScopeType = r.ScopeType, BranchId = r.BranchId, BranchName = r.BranchId is { } b ? branchNames.GetValueOrDefault(b) : null,
        }).OrderByDescending(r => r.IsPrimary).ThenBy(r => r.RoleName).ToList();

        var primary = user.Roles.FirstOrDefault(r => r.IsPrimary);
        user.ScopeType = primary?.ScopeType ?? ScopeTypes.AllBranches;
        user.BranchId = primary?.BranchId;
        user.BranchName = primary?.BranchName;

        if (user.LinkedPartnerId is { } partnerId)
        {
            var partner = await partners.FindAsync(partnerId);
            user.LinkedPartnerName = partner?.DisplayName;
        }

        return user;
    }

    public async Task<List<RoleOptionModel>> GetAssignableRolesAsync(CallerContext caller)
    {
        var roles = await db.Roles.AsNoTracking().Where(r => r.IsActive).OrderBy(r => r.Name).ToListAsync();

        if (!caller.IsSuperAdmin)
        {
            var codesByRole = (await (
                from rp in db.RolePermissions.AsNoTracking()
                join p in db.Permissions.AsNoTracking() on rp.PermissionID equals p.PermissionID
                select new { rp.RoleID, p.Code }).ToListAsync())
                .GroupBy(x => x.RoleID)
                .ToDictionary(g => g.Key, g => g.Select(x => x.Code).ToList());

            // Same rule RoleGuard.EnsureCanGrantRoleAsync enforces on assignment.
            roles = roles.Where(r =>
                r.RoleCode != RoleCodes.SuperAdmin &&
                (!codesByRole.TryGetValue(r.RoleID, out var codes) || codes.All(caller.Permissions.Contains))).ToList();
        }

        return roles.Select(r => new RoleOptionModel { RoleId = r.RoleID, Name = r.Name, RoleCode = r.RoleCode }).ToList();
    }

    public async Task PatchAsync(int userId, PatchUserRequest request, CallerContext caller)
    {
        var user = await LoadManageableAsync(userId, caller);

        if (request.FirstName is not null)
        {
            if (string.IsNullOrWhiteSpace(request.FirstName))
                throw new BadRequestException("First name cannot be empty.");
            user.FirstName = request.FirstName.Trim();
        }
        if (request.LastName is not null) user.LastName = request.LastName.Trim();
        if (request.Phone is not null) user.Phone = request.Phone.Trim();
        if (request.Department is not null) user.Department = request.Department.Trim();

        var deactivating = false;
        if (request.IsActive is { } isActive && isActive != user.IsActive)
        {
            if (!isActive && userId == caller.UserId)
                throw new BadRequestException("You cannot deactivate your own account.");
            if (!isActive) await EnsureAdministratorRemainsAsync(userId, roleId: null);
            user.IsActive = isActive;
            deactivating = !isActive;
        }

        user.UpdatedDate = DateTime.UtcNow;
        await db.SaveChangesAsync();

        if (deactivating) await RevokeSessionsAsync(userId);
    }

    public async Task AssignRoleAsync(int userId, int roleId, CallerContext caller)
    {
        if (userId == caller.UserId)
            throw new BadRequestException("You cannot change your own role.");

        var user = await LoadManageableAsync(userId, caller);
        var role = await db.Roles.FirstOrDefaultAsync(r => r.RoleID == roleId && r.IsActive)
            ?? throw new BadRequestException("Choose an active role.");
        await RoleGuard.EnsureCanGrantRoleAsync(db, role, caller);

        // Only a risk if the new role does not itself carry ROLE_MANAGE — if it does, the user is still an administrator afterwards.
        if (!await RoleHasPermissionAsync(role.RoleID, PermissionCodes.ROLE_MANAGE))
            await EnsureAdministratorRemainsAsync(userId, roleId: null);

        // UserRoles is the full set the user holds; RoleID stays their primary role. Keep the two in
        // step, carrying the old primary role's scope over to the new one.
        var held = await db.UserRoles.Where(ur => ur.UserID == user.UserID).ToListAsync();
        var scope = held.FirstOrDefault(ur => ur.RoleID == user.RoleID)?.ScopeType ?? ScopeTypes.AllBranches;
        var branch = held.FirstOrDefault(ur => ur.RoleID == user.RoleID)?.BranchId;
        db.UserRoles.RemoveRange(held.Where(ur => ur.RoleID != role.RoleID));
        if (held.All(ur => ur.RoleID != role.RoleID))
            db.UserRoles.Add(new UserRole { UserID = user.UserID, RoleID = role.RoleID, ScopeType = scope, BranchId = branch, TenantId = user.TenantId });

        user.RoleID = role.RoleID;
        user.UpdatedDate = DateTime.UtcNow;
        await db.SaveChangesAsync();

        // The old role's permissions are baked into the user's tokens; end their sessions so the new
        // role takes effect at their next refresh instead of lingering.
        await RevokeSessionsAsync(userId);
    }

    public async Task AddRoleAsync(int userId, AddRoleRequest request, CallerContext caller)
    {
        var user = await LoadManageableAsync(userId, caller);
        var role = await db.Roles.FirstOrDefaultAsync(r => r.RoleID == request.RoleId && r.IsActive)
            ?? throw new BadRequestException("Choose an active role.");
        await RoleGuard.EnsureCanGrantRoleAsync(db, role, caller);

        if (await db.UserRoles.AnyAsync(ur => ur.UserID == userId && ur.RoleID == role.RoleID))
            throw new ConflictException("The user already holds this role.");

        var (scopeType, branchId) = await ResolveScopeAsync(request.ScopeType, request.BranchId);
        db.UserRoles.Add(new UserRole { UserID = userId, RoleID = role.RoleID, ScopeType = scopeType, BranchId = branchId, TenantId = user.TenantId });
        await db.SaveChangesAsync();

        // A newly granted permission is only useful once it rides a fresh token.
        await RevokeSessionsAsync(userId);
    }

    public async Task RemoveRoleAsync(int userId, int roleId, CallerContext caller)
    {
        var user = await LoadManageableAsync(userId, caller);
        var held = await db.UserRoles.Where(ur => ur.UserID == userId).ToListAsync();
        if (held.Count <= 1)
            throw new BadRequestException("A user must hold at least one role. Assign a different one before removing this.");
        var row = held.FirstOrDefault(ur => ur.RoleID == roleId)
            ?? throw new NotFoundException("The user does not hold this role.");
        var role = await db.Roles.FirstAsync(r => r.RoleID == roleId);
        await RoleGuard.EnsureCanGrantRoleAsync(db, role, caller);   // the same "hand out only what you hold" rule applies to taking it away

        await EnsureAdministratorRemainsAsync(userId, roleId);

        db.UserRoles.Remove(row);
        if (user.RoleID == roleId)
            // The primary role just left; another one they still hold becomes it, keeping its own scope.
            user.RoleID = held.First(ur => ur.RoleID != roleId).RoleID;
        user.UpdatedDate = DateTime.UtcNow;
        await db.SaveChangesAsync();

        await RevokeSessionsAsync(userId);
    }

    public async Task SetScopeAsync(int userId, SetScopeRequest request, CallerContext caller)
    {
        var user = await LoadManageableAsync(userId, caller);
        var (scopeType, branchId) = await ResolveScopeAsync(request.ScopeType, request.BranchId);

        var row = await db.UserRoles.FirstOrDefaultAsync(ur => ur.UserID == userId && ur.RoleID == user.RoleID)
            ?? throw new NotFoundException("The user's primary role assignment was not found.");
        row.ScopeType = scopeType;
        row.BranchId = branchId;
        await db.SaveChangesAsync();

        // A narrower scope must take effect at once, not linger for up to the access token's own lifetime.
        await RevokeSessionsAsync(userId);
    }

    public async Task SetDriverLinkAsync(int userId, SetDriverLinkRequest request, CallerContext caller)
    {
        var user = await LoadManageableAsync(userId, caller);
        user.LinkedPartnerId = await ResolveDriverLinkAsync(request.PartnerId);
        user.UpdatedDate = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task<List<EffectivePermissionModel>> GetEffectivePermissionsAsync(int userId)
    {
        if (!await db.UserAccounts.AnyAsync(u => u.UserID == userId && !u.IsDeleted))
            throw new NotFoundException("User not found.");

        var rows = await (
            from ur in db.UserRoles.AsNoTracking()
            where ur.UserID == userId
            join r in db.Roles.AsNoTracking() on ur.RoleID equals r.RoleID
            join rp in db.RolePermissions.AsNoTracking() on r.RoleID equals rp.RoleID
            join p in db.Permissions.AsNoTracking() on rp.PermissionID equals p.PermissionID
            select new { RoleName = r.Name, p.Code, PermissionName = p.Name, p.Module, p.Level }).ToListAsync();

        return rows.GroupBy(r => r.Code)
            .Select(g => new EffectivePermissionModel
            {
                Code = g.Key, Name = g.First().PermissionName, Module = g.First().Module, Level = g.First().Level,
                GrantedByRoles = g.Select(r => r.RoleName).Distinct().OrderBy(n => n).ToList(),
            })
            .OrderBy(p => p.Module).ThenBy(p => p.Name)
            .ToList();
    }

    public async Task<UserDetailModel> ResetPasswordAsync(int userId, CallerContext caller)
    {
        if (userId == caller.UserId)
            throw new BadRequestException("Use 'change password' to change your own password.");

        var user = await LoadManageableAsync(userId, caller);

        var rawToken = TokenHelper.NewToken();
        user.InviteTokenHash = TokenHelper.Sha256Hex(rawToken);
        user.InviteTokenExpiresAt = DateTime.UtcNow.Add(InviteTtl);
        user.UpdatedDate = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await RevokeSessionsAsync(userId);

        var link = notifier.InviteLink(rawToken);
        await notifier.SendInviteAsync(user, link, isReset: true);

        var detail = await GetAsync(userId);
        detail.InviteLink = link;
        return detail;
    }

    public async Task DeleteAsync(int userId, CallerContext caller)
    {
        if (userId == caller.UserId)
            throw new BadRequestException("You cannot delete your own account.");

        var user = await LoadManageableAsync(userId, caller);
        await EnsureAdministratorRemainsAsync(userId, roleId: null);

        user.IsDeleted = true;
        user.IsActive = false;
        user.InviteTokenHash = null;
        user.InviteTokenExpiresAt = null;
        // Frees the email for reuse; the unique index would otherwise keep it forever.
        var tombstone = $"deleted_{user.UserID}_{user.Email}";
        user.Email = tombstone.Length > 256 ? tombstone[..256] : tombstone;
        user.UpdatedDate = DateTime.UtcNow;
        await db.SaveChangesAsync();

        await RevokeSessionsAsync(userId);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Loads a user the caller is allowed to modify. Tenant filters already hide other tenants' users
    /// (a miss is a 404, not a 403, so ids do not leak); on top of that, only a Super Admin may touch
    /// another Super Admin.
    /// </summary>
    private async Task<UserAccount> LoadManageableAsync(int userId, CallerContext caller)
    {
        var user = await db.UserAccounts.FirstOrDefaultAsync(u => u.UserID == userId && !u.IsDeleted)
            ?? throw new NotFoundException("User not found.");

        if (!caller.IsSuperAdmin && await superAdmins.IsSuperAdminAsync(userId))
            throw new ForbiddenException("Only a Super Admin can modify a Super Admin.");

        return user;
    }

    private Task RevokeSessionsAsync(int userId) =>
        db.UserSessions
            .Where(s => s.UserID == userId && s.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTime.UtcNow));

    private Task<bool> RoleHasPermissionAsync(int roleId, string permissionCode) =>
        (from rp in db.RolePermissions
         join p in db.Permissions on rp.PermissionID equals p.PermissionID
         where rp.RoleID == roleId && p.Code == permissionCode
         select rp.RolePermissionID).AnyAsync();

    /// <summary>
    /// BR-SEC-008: the tenant must always keep at least one active user able to manage roles. Checks against
    /// the database as it stands now, as if this one relationship were already gone — either one (user, role)
    /// membership (<paramref name="roleId"/> given), or this whole user's active status (<paramref name="roleId"/> null).
    /// Explicitly scoped to the affected user's own tenant and never the ambient one: a Super Admin's request
    /// bypasses the tenant query filter entirely (by design, for cross-tenant administration), which would
    /// otherwise let this check see every tenant's administrators and never refuse anything.
    /// </summary>
    private async Task EnsureAdministratorRemainsAsync(int userId, int? roleId)
    {
        var tenantId = await db.UserAccounts.IgnoreQueryFilters().Where(u => u.UserID == userId).Select(u => u.TenantId).FirstOrDefaultAsync();

        var adminRoleIds = await (
            from rp in db.RolePermissions
            join p in db.Permissions on rp.PermissionID equals p.PermissionID
            where p.Code == PermissionCodes.ROLE_MANAGE
            select rp.RoleID).ToListAsync();
        if (adminRoleIds.Count == 0) return; // nothing currently grants it — this change cannot be what removes it

        var holders = await (
            from ur in db.UserRoles.IgnoreQueryFilters()
            where adminRoleIds.Contains(ur.RoleID)
            join u in db.UserAccounts.IgnoreQueryFilters() on ur.UserID equals u.UserID
            where u.IsActive && !u.IsDeleted && u.TenantId == tenantId
            select new { ur.UserID, ur.RoleID }).ToListAsync();

        var remaining = roleId is { } r ? holders.Where(h => !(h.UserID == userId && h.RoleID == r)) : holders.Where(h => h.UserID != userId);
        if (!remaining.Any())
            throw new ConflictException("This would leave nobody able to manage roles for this tenant. Give ROLE_MANAGE to someone else first.");
    }

    /// <summary>§23B.4: validates a scope choice against the type list and, for Own branch, the branch itself. Defaults to All branches.</summary>
    private async Task<(string ScopeType, Guid? BranchId)> ResolveScopeAsync(string? scopeType, Guid? branchId)
    {
        var type = string.IsNullOrWhiteSpace(scopeType) ? ScopeTypes.AllBranches : scopeType;
        if (!ScopeTypes.All.Contains(type))
            throw new BadRequestException($"Scope must be one of: {string.Join(", ", ScopeTypes.All)}.");

        if (type != ScopeTypes.OwnBranch) return (type, null);

        if (branchId is not { } id) throw new BadRequestException("Choose a branch for Own branch scope.");
        var branch = await branches.FindAsync(id) ?? throw new BadRequestException("Branch not found.");
        return (type, branch.Id);
    }

    /// <summary>§23B.1, OQ-20: the linked partner must exist and carry the Driver role — this link is what "own vehicles" scope filters by.</summary>
    private async Task<int?> ResolveDriverLinkAsync(int? partnerId)
    {
        if (partnerId is not { } id) return null;
        var partner = await partners.FindAsync(id) ?? throw new BadRequestException("Partner not found.");
        if (!partner.HasRole("Driver")) throw new BadRequestException("Only a partner with the Driver role can be linked to a user.");
        return id;
    }

    // NEVER call IgnoreQueryFilters() inside one of these projections: it is a whole-query switch,
    // not a per-table one, so a subquery that ignores the Roles filter also silently drops the tenant
    // filter on UserAccounts and returns every tenant's users. The plain Roles filter is enough — a
    // user in the caller's tenant holds a global role or one of that tenant's own, both visible.
    private IQueryable<UserListItemModel> ProjectToList(IQueryable<UserAccount> query) =>
        query.Select(u => new UserListItemModel
        {
            UserId = u.UserID,
            TenantId = u.TenantId,
            FirstName = u.FirstName,
            LastName = u.LastName,
            Email = u.Email,
            Department = u.Department,
            IsActive = u.IsActive,
            InvitePending = u.InviteTokenHash != null,
            CreatedDate = u.CreatedDate,
            LastLoginAt = u.LastLoginAt,
            Role = db.Roles
                .Where(r => r.RoleID == u.RoleID)
                .Select(r => new DropDownVM { ID = r.RoleID, Value = r.Name })
                .FirstOrDefault(),
            ScopeType = db.UserRoles.Where(ur => ur.UserID == u.UserID && ur.RoleID == u.RoleID).Select(ur => ur.ScopeType).FirstOrDefault() ?? ScopeTypes.AllBranches,
            BranchId = db.UserRoles.Where(ur => ur.UserID == u.UserID && ur.RoleID == u.RoleID).Select(ur => ur.BranchId).FirstOrDefault()
        });

    private static string NormalizeEmail(string? raw)
    {
        var email = (raw ?? string.Empty).Trim().ToLowerInvariant();
        if (email.Length == 0 || email.Length > 256)
            throw new BadRequestException("A valid email is required.");
        try
        {
            if (new MailAddress(email).Address != email) throw new FormatException();
        }
        catch (FormatException)
        {
            throw new BadRequestException("A valid email is required.");
        }
        return email;
    }
}
