using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using VMS.Modules.Auth.Data;
using VMS.Modules.Auth.Domain;
using VMS.Modules.Auth.Infrastructure;
using VMS.Shared.Common;
using VMS.Shared.Exceptions;

namespace VMS.Modules.Auth.Services;

/// <summary>The Auth half of tenant creation and deactivation — see <see cref="ITenantUserProvisioningService"/>.</summary>
internal sealed class TenantUserProvisioningService(
    AuthDbContext db,
    IPasswordHasher<UserAccount> hasher,
    AuthNotifier notifier) : ITenantUserProvisioningService
{
    private static readonly TimeSpan InviteTtl = TimeSpan.FromHours(72);

    public Task<bool> EmailExistsAsync(string email) =>
        db.UserAccounts.IgnoreQueryFilters().AnyAsync(u => u.Email == email.Trim().ToLower());

    public async Task<TenantAdminInvite> CreateTenantAdminAsync(
        Guid tenantId, string firstName, string? lastName, string email, int createdBy)
    {
        var roleId = await db.Roles.IgnoreQueryFilters()
            .Where(r => r.IsGlobal && r.RoleCode == RoleCodes.TenantAdmin)
            .Select(r => (int?)r.RoleID)
            .FirstOrDefaultAsync()
            ?? throw new InvalidOperationException("The TENANT_ADMIN role has not been seeded.");

        var rawToken = TokenHelper.NewToken();
        var user = new UserAccount
        {
            FirstName = firstName,
            LastName = lastName,
            Email = email.Trim().ToLowerInvariant(),
            RoleID = roleId,
            TenantId = tenantId,
            IsActive = false,
            CreatedBy = createdBy,
            CreatedDate = DateTime.UtcNow,
            InviteTokenHash = TokenHelper.Sha256Hex(rawToken),
            InviteTokenExpiresAt = DateTime.UtcNow.Add(InviteTtl)
        };
        user.PasswordHash = hasher.HashPassword(user, TokenHelper.NewToken());

        db.UserAccounts.Add(user);
        await db.SaveChangesAsync();

        var link = notifier.InviteLink(rawToken);
        await notifier.SendInviteAsync(user, link, isReset: false);
        return new TenantAdminInvite(user.UserID, link);
    }

    public Task RevokeSessionsForTenantAsync(Guid tenantId) =>
        db.UserSessions.IgnoreQueryFilters()
            .Where(s => s.TenantId == tenantId && s.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTime.UtcNow));
}
