using System.Net.Mail;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using VMS.Modules.Auth.Data;
using VMS.Modules.Auth.Domain;
using VMS.Modules.Auth.Infrastructure;
using VMS.Modules.Auth.Models;
using VMS.Shared.Common;
using VMS.Shared.Exceptions;
using VMS.Shared.Pagination;

namespace VMS.Modules.Auth.Services;

internal sealed class UserService(
    AuthDbContext db,
    IPasswordHasher<UserAccount> hasher,
    ISuperAdminService superAdmins,
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
                Role = db.Roles
                    .Where(r => r.RoleID == u.RoleID)
                    .Select(r => new DropDownVM { ID = r.RoleID, Value = r.Name })
                    .FirstOrDefault()
            })
            .FirstOrDefaultAsync();

        return user ?? throw new NotFoundException("User not found.");
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

        user.RoleID = role.RoleID;
        user.UpdatedDate = DateTime.UtcNow;
        await db.SaveChangesAsync();

        // The old role's permissions are baked into the user's tokens; end their sessions so the new
        // role takes effect at their next refresh instead of lingering.
        await RevokeSessionsAsync(userId);
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
                .FirstOrDefault()
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
