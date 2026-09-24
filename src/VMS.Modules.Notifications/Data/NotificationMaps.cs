using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VMS.Modules.Notifications.Domain;

namespace VMS.Modules.Notifications.Data;

internal sealed class NotificationRuleMap : IEntityTypeConfiguration<NotificationRule>
{
    public void Configure(EntityTypeBuilder<NotificationRule> b)
    {
        b.ToTable("NotificationRules");
        b.HasKey(x => x.NotificationRuleId);
        b.Property(x => x.EventType).HasMaxLength(30).IsRequired();
        b.Property(x => x.Name).HasMaxLength(80).IsRequired();
        b.Property(x => x.RecipientPermission).HasMaxLength(60).IsRequired();
        b.Property(x => x.EscalationPermission).HasMaxLength(60);

        // BR-NOT-001: one rule per tenant per event type.
        b.HasIndex(x => new { x.TenantId, x.EventType }).IsUnique();
    }
}

internal sealed class NotificationMap : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> b)
    {
        b.ToTable("Notifications");
        b.HasKey(x => x.NotificationId);
        b.Property(x => x.EventType).HasMaxLength(30).IsRequired();
        b.Property(x => x.Title).HasMaxLength(150).IsRequired();
        b.Property(x => x.Message).HasMaxLength(500).IsRequired();
        b.Property(x => x.SourceEntity).HasMaxLength(40).IsRequired();
        b.Property(x => x.SourceId).HasMaxLength(40).IsRequired();
        b.Property(x => x.OwnerType).HasMaxLength(20).IsRequired();

        // The dedup key (BR-NOT-002): is this (user, source row, event, escalation-or-not) already notified? A real
        // uniqueness constraint, not just an in-memory check — the hourly job and an admin's "run now" can race, the
        // same way BR-VH-030 protects a recurring charge entry's period key.
        b.HasIndex(x => new { x.TenantId, x.UserId, x.SourceEntity, x.SourceId, x.EventType, x.IsEscalation }).IsUnique().HasDatabaseName("UX_Notifications_Dedup");
        // The in-app list's own query: this user's notifications, newest first, unread count.
        b.HasIndex(x => new { x.TenantId, x.UserId, x.IsRead, x.OccurredOn });
    }
}
