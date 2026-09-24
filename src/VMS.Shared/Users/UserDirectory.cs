namespace VMS.Shared.Users;

/// <summary>What another module needs to know about a user it wants to reach — no more than a notification needs.</summary>
public sealed record UserInfo(int UserId, string Name, string Email);

/// <summary>
/// Looks up the tenant's own active users for the modules that need to reach some of them — Notifications is the only
/// consumer today (who should be told a document is expiring, a charge is due) — so those modules never read the Auth
/// module's user or role tables themselves. Only the caller's own tenant's users are ever returned.
/// </summary>
public interface IUserDirectory
{
    /// <summary>Every active user (not deleted, not invite-pending) who holds the given permission, through any role they hold, directly or via a Super Admin's blanket access excluded — a notification is for a tenant's own people.</summary>
    Task<IReadOnlyList<UserInfo>> FindActiveByPermissionAsync(string permissionCode, CancellationToken cancellationToken = default);
}
