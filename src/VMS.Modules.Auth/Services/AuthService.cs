using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using VMS.Modules.Auth.Data;
using VMS.Modules.Auth.Domain;
using VMS.Modules.Auth.Infrastructure;
using VMS.Modules.Auth.Models;
using VMS.Shared.Authorization;
using VMS.Shared.Common;
using VMS.Shared.Exceptions;

namespace VMS.Modules.Auth.Services;

internal sealed class AuthService(
    AuthDbContext db,
    ITokenService tokens,
    IPasswordHasher<UserAccount> hasher,
    ITenantStatusService tenants,
    ISuperAdminService superAdmins,
    AuthNotifier notifier) : IAuthService
{
    private const int LockoutThreshold = 5;
    private const int MaxResetAttempts = 5;
    private static readonly TimeSpan LockoutWindow = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan RefreshTokenTtl = TimeSpan.FromDays(7);
    private static readonly TimeSpan ResetCodeTtl = TimeSpan.FromMinutes(30);

    // Verified against when the email is unknown, so an unknown email costs the same time as a wrong
    // password and login cannot be used to discover which emails have accounts.
    private static readonly UserAccount DummyUser = new();
    private string? _dummyHash;
    private string DummyHash => _dummyHash ??= hasher.HashPassword(DummyUser, Guid.NewGuid().ToString("N"));

    // ── Login / refresh / logout ─────────────────────────────────────────────

    public async Task<LoginResponseModel> LoginAsync(LoginRequestModel request)
    {
        var email = (request.Email ?? string.Empty).Trim().ToLowerInvariant();
        var user = await db.UserAccounts.FirstOrDefaultAsync(u => u.Email == email && !u.IsDeleted);

        if (user is null)
        {
            hasher.VerifyHashedPassword(DummyUser, DummyHash, request.Password ?? string.Empty);
            throw new UnauthorizedException("Invalid email or password.");
        }

        if (user.LockedUntil is { } lockedUntil && lockedUntil > DateTime.UtcNow)
            throw new AccountLockedException(lockedUntil);

        if (hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password ?? string.Empty) == PasswordVerificationResult.Failed)
        {
            await RegisterFailedAttemptAsync(user);
            throw new UnauthorizedException("Invalid email or password.");
        }

        if (!user.IsActive)
            throw new UnauthorizedException("This account is not active.");
        if (!await tenants.IsTenantActiveAsync(user.TenantId))
            throw new UnauthorizedException("This tenant has been deactivated.");

        user.FailedLoginAttempts = 0;
        user.LastFailedAt = null;
        user.LockedUntil = null;
        user.LastLoginAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var session = await IssueSessionAsync(user);
        return new LoginResponseModel
        {
            AccessToken = session.AccessToken,
            RefreshToken = session.RefreshToken,
            ExpiresIn = TokenService.AccessTokenSeconds,
            User = await BuildCurrentUserAsync(user)
        };
    }

    public async Task<RefreshResponseModel> RefreshAsync(string rawRefreshToken)
    {
        var hash = TokenHelper.Sha256Hex(rawRefreshToken ?? string.Empty);
        var session = await db.UserSessions.FirstOrDefaultAsync(s => s.TokenHash == hash);
        if (session is null || session.ExpiresAt <= DateTime.UtcNow)
            throw new UnauthorizedException("Invalid or expired refresh token.");

        if (session.RevokedAt is not null)
        {
            // A rotated-out token is being replayed: either a client bug or a stolen token. Either way
            // the whole session family is suspect, so end every session this user has.
            await RevokeAllSessionsAsync(session.UserID);
            throw new UnauthorizedException("Invalid or expired refresh token.");
        }

        var user = await db.UserAccounts.FirstOrDefaultAsync(u => u.UserID == session.UserID);
        if (user is null || !user.IsActive || user.IsDeleted || !await tenants.IsTenantActiveAsync(user.TenantId))
            throw new UnauthorizedException("Invalid or expired refresh token.");

        session.RevokedAt = DateTime.UtcNow;
        var next = await IssueSessionAsync(user);

        return new RefreshResponseModel
        {
            AccessToken = next.AccessToken,
            RefreshToken = next.RefreshToken,
            ExpiresIn = TokenService.AccessTokenSeconds
        };
    }

    public async Task LogoutAsync(string rawRefreshToken)
    {
        var hash = TokenHelper.Sha256Hex(rawRefreshToken ?? string.Empty);
        var session = await db.UserSessions.FirstOrDefaultAsync(s => s.TokenHash == hash && s.RevokedAt == null);
        if (session is null) return;

        session.RevokedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task<CurrentUserModel> GetCurrentUserAsync(int userId)
    {
        var user = await db.UserAccounts.AsNoTracking().FirstOrDefaultAsync(u => u.UserID == userId && !u.IsDeleted)
            ?? throw new NotFoundException("User not found.");
        return await BuildCurrentUserAsync(user);
    }

    // ── Password flows ───────────────────────────────────────────────────────

    public async Task ChangePasswordAsync(int userId, ChangePasswordRequest request)
    {
        var user = await db.UserAccounts.FirstOrDefaultAsync(u => u.UserID == userId && !u.IsDeleted)
            ?? throw new NotFoundException("User not found.");

        if (hasher.VerifyHashedPassword(user, user.PasswordHash, request.CurrentPassword ?? string.Empty) == PasswordVerificationResult.Failed)
            throw new BadRequestException("Current password is incorrect.");
        PasswordPolicy.Validate(request.NewPassword);

        user.PasswordHash = hasher.HashPassword(user, request.NewPassword);
        user.UpdatedDate = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await RevokeAllSessionsAsync(userId);
    }

    /// <summary>Always completes without revealing whether the email has an account.</summary>
    public async Task ForgotPasswordAsync(string email)
    {
        var normalized = (email ?? string.Empty).Trim().ToLowerInvariant();
        var user = await db.UserAccounts.FirstOrDefaultAsync(u => u.Email == normalized && u.IsActive && !u.IsDeleted);
        if (user is null) return;

        var code = TokenHelper.NewSixDigitCode();
        user.PasswordResetCodeHash = TokenHelper.Sha256Hex(code);
        user.PasswordResetCodeExpiresAt = DateTime.UtcNow.Add(ResetCodeTtl);
        user.PasswordResetAttempts = 0;
        await db.SaveChangesAsync();

        await notifier.SendResetCodeAsync(user, code);
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request)
    {
        const string invalid = "The code is invalid or has expired.";

        var normalized = (request.Email ?? string.Empty).Trim().ToLowerInvariant();
        var user = await db.UserAccounts.FirstOrDefaultAsync(u => u.Email == normalized && !u.IsDeleted);

        if (user?.PasswordResetCodeHash is null
            || user.PasswordResetCodeExpiresAt is null
            || user.PasswordResetCodeExpiresAt < DateTime.UtcNow)
            throw new BadRequestException(invalid);

        if (!TokenHelper.HashesEqual(user.PasswordResetCodeHash, TokenHelper.Sha256Hex(request.Code ?? string.Empty)))
        {
            // A 6-digit code is only safe if it dies after a few wrong guesses.
            user.PasswordResetAttempts++;
            if (user.PasswordResetAttempts >= MaxResetAttempts)
            {
                user.PasswordResetCodeHash = null;
                user.PasswordResetCodeExpiresAt = null;
            }
            await db.SaveChangesAsync();
            throw new BadRequestException(invalid);
        }

        PasswordPolicy.Validate(request.NewPassword);

        user.PasswordHash = hasher.HashPassword(user, request.NewPassword);
        user.PasswordResetCodeHash = null;
        user.PasswordResetCodeExpiresAt = null;
        user.PasswordResetAttempts = 0;
        user.FailedLoginAttempts = 0;
        user.LockedUntil = null;
        user.UpdatedDate = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await RevokeAllSessionsAsync(user.UserID);
    }

    public async Task AcceptInviteAsync(string token, string newPassword)
    {
        var hash = TokenHelper.Sha256Hex(token ?? string.Empty);
        var user = await db.UserAccounts.FirstOrDefaultAsync(u => u.InviteTokenHash == hash && !u.IsDeleted)
            ?? throw new BadRequestException("This invite link is invalid.");

        if (user.InviteTokenExpiresAt is null || user.InviteTokenExpiresAt < DateTime.UtcNow)
            throw new BadRequestException("This invite link has expired.");

        PasswordPolicy.Validate(newPassword);

        user.PasswordHash = hasher.HashPassword(user, newPassword);
        user.IsActive = true;
        user.InviteTokenHash = null;
        user.InviteTokenExpiresAt = null;
        user.FailedLoginAttempts = 0;
        user.LockedUntil = null;
        user.UpdatedDate = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await RevokeAllSessionsAsync(user.UserID);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task RegisterFailedAttemptAsync(UserAccount user)
    {
        var now = DateTime.UtcNow;
        if (user.LastFailedAt is null || now - user.LastFailedAt.Value > LockoutWindow)
        {
            user.FailedLoginAttempts = 1;
            user.LastFailedAt = now;
        }
        else
        {
            user.FailedLoginAttempts++;
        }

        if (user.FailedLoginAttempts >= LockoutThreshold)
        {
            user.LockedUntil = now.Add(LockoutDuration);
            user.FailedLoginAttempts = 0;
            user.LastFailedAt = null;
        }

        await db.SaveChangesAsync();
    }

    private async Task<(string AccessToken, string RefreshToken)> IssueSessionAsync(UserAccount user)
    {
        var (roleName, permissions, scopeType, scopeBranchId) = await LoadRoleAsync(user.UserID, user.RoleID);
        var isSuperAdmin = await superAdmins.IsSuperAdminAsync(user.UserID);
        var accessToken = tokens.GenerateAccessToken(user, roleName, permissions, isSuperAdmin, scopeType, scopeBranchId);
        var refreshToken = tokens.GenerateRefreshToken();

        var now = DateTime.UtcNow;
        db.UserSessions.Add(new UserSession
        {
            Id = Guid.NewGuid(),
            UserID = user.UserID,
            TenantId = user.TenantId,
            TokenHash = TokenHelper.Sha256Hex(refreshToken),
            CreatedAt = now,
            ExpiresAt = now.Add(RefreshTokenTtl)
        });

        // Housekeeping while we are here: drop this user's expired sessions.
        await db.UserSessions
            .Where(s => s.UserID == user.UserID && s.ExpiresAt < now)
            .ExecuteDeleteAsync();

        await db.SaveChangesAsync();
        return (accessToken, refreshToken);
    }

    /// <summary>
    /// The primary role's name (for the "roleId"/"roleName" claims and the current-user display), the effective
    /// permission set — the union across every active role the user holds (§23B.1), not the primary role alone —
    /// and their scope: the widest one across those same roles (§23B.4).
    /// </summary>
    private async Task<(string RoleName, List<string> Permissions, string ScopeType, Guid? ScopeBranchId)> LoadRoleAsync(int userId, int primaryRoleId)
    {
        var roleName = await db.Roles.IgnoreQueryFilters().AsNoTracking()
            .Where(r => r.RoleID == primaryRoleId).Select(r => r.Name).FirstOrDefaultAsync() ?? string.Empty;

        // IgnoreQueryFilters() once, up front: this codebase's tenant filter is a whole-query switch, not
        // per-table, and this query needs it off both here and on the Role join below — login runs before
        // the ambient tenant context is established, so filtering by it here would silently return nothing.
        // Safe regardless: every row is already scoped to this one known, already-authenticated userId.
        var activeRoles = await (
            from ur in db.UserRoles.IgnoreQueryFilters().AsNoTracking()
            where ur.UserID == userId
            join r in db.Roles.AsNoTracking() on ur.RoleID equals r.RoleID
            where r.IsActive
            select new { ur.ScopeType, ur.BranchId, r.RoleID }).ToListAsync();

        var permissions = activeRoles.Count == 0 ? [] : await (
            from rp in db.RolePermissions.AsNoTracking()
            where activeRoles.Select(a => a.RoleID).Contains(rp.RoleID)
            join p in db.Permissions.AsNoTracking() on rp.PermissionID equals p.PermissionID
            select p.Code).Distinct().ToListAsync();

        var scopeType = ScopeTypes.Widest(activeRoles.Select(a => a.ScopeType));
        var scopeBranchId = scopeType == ScopeTypes.OwnBranch ? activeRoles.FirstOrDefault(a => a.ScopeType == ScopeTypes.OwnBranch)?.BranchId : null;

        return (roleName, permissions, scopeType, scopeBranchId);
    }

    private async Task<CurrentUserModel> BuildCurrentUserAsync(UserAccount user)
    {
        var (roleName, permissions, _, _) = await LoadRoleAsync(user.UserID, user.RoleID);
        return new CurrentUserModel
        {
            UserId = user.UserID,
            TenantId = user.TenantId,
            Email = user.Email,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Phone = user.Phone,
            Department = user.Department,
            Role = new DropDownVM { ID = user.RoleID, Value = roleName },
            IsSuperAdmin = await superAdmins.IsSuperAdminAsync(user.UserID),
            Permissions = permissions
        };
    }

    private Task RevokeAllSessionsAsync(int userId) =>
        db.UserSessions
            .Where(s => s.UserID == userId && s.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTime.UtcNow));
}
