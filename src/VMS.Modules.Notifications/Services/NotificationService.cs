using Microsoft.EntityFrameworkCore;
using VMS.Modules.Notifications.Data;
using VMS.Modules.Notifications.Domain;
using VMS.Modules.Notifications.Models;
using VMS.Shared.Common;
using VMS.Shared.Exceptions;

namespace VMS.Modules.Notifications.Services;

/// <summary>The signed-in user's own in-app notification list (S7-NOT-06, FSD §23.4): newest first, with an unread count for the shell's bell icon.</summary>
public interface INotificationService
{
    Task<List<NotificationModel>> ListAsync(int userId, bool unreadOnly = false, int take = 50);
    Task<int> UnreadCountAsync(int userId);
    Task MarkReadAsync(int userId, int notificationId);
    Task MarkAllReadAsync(int userId);
}

internal sealed class NotificationService(NotificationDbContext db, ITenantContext tenantContext) : INotificationService
{
    private Guid Tenant => tenantContext.TenantId;

    public async Task<List<NotificationModel>> ListAsync(int userId, bool unreadOnly = false, int take = 50)
    {
        var query = db.Notifications.AsNoTracking().Where(n => n.TenantId == Tenant && n.UserId == userId);
        if (unreadOnly) query = query.Where(n => !n.IsRead);
        var rows = await query.OrderByDescending(n => n.OccurredOn).Take(Math.Clamp(take, 1, 200)).ToListAsync();
        return rows.Select(ToModel).ToList();
    }

    private static NotificationModel ToModel(Notification n) => new()
    {
        Id = n.NotificationId, EventType = n.EventType, Title = n.Title, Message = n.Message,
        OwnerType = n.OwnerType, OwnerId = n.OwnerId, IsEscalation = n.IsEscalation, OccurredOn = n.OccurredOn, IsRead = n.IsRead,
    };

    public Task<int> UnreadCountAsync(int userId) =>
        db.Notifications.AsNoTracking().CountAsync(n => n.TenantId == Tenant && n.UserId == userId && !n.IsRead);

    public async Task MarkReadAsync(int userId, int notificationId)
    {
        // Only the recipient's own row: a user marking someone else's notification read (by guessing an id) does nothing rather than erroring, so no information about another user's inbox leaks either way.
        var row = await db.Notifications.FirstOrDefaultAsync(n => n.TenantId == Tenant && n.UserId == userId && n.NotificationId == notificationId)
            ?? throw new NotFoundException("Notification not found.");
        if (row.IsRead) return;
        row.IsRead = true;
        row.ReadOn = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task MarkAllReadAsync(int userId)
    {
        var unread = await db.Notifications.Where(n => n.TenantId == Tenant && n.UserId == userId && !n.IsRead).ToListAsync();
        if (unread.Count == 0) return;
        var now = DateTime.UtcNow;
        foreach (var n in unread) { n.IsRead = true; n.ReadOn = now; }
        await db.SaveChangesAsync();
    }
}
