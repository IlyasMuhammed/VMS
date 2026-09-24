using VMS.Shared.Common;
using VMS.Shared.Notifications;

namespace VMS.Modules.Notifications.Domain;

/// <summary>
/// The notification rule master (FSD §9, §19A.6, §23.4): one row per event type, admin-configurable. "All lead times are
/// values in the notification rule master, not constants in code" (§23.4's closing line) — so a document's own
/// <c>RenewalLeadDays</c> (when its status turns Expiring Soon) and this rule's <see cref="LeadDays"/> (when someone is told
/// about it) are deliberately two separate, independently configured numbers (FR-VH-008).
/// </summary>
internal class NotificationRule : ITenantScopedEntity
{
    public int NotificationRuleId { get; set; }
    public Guid TenantId { get; set; }

    /// <see cref="NotificationEventTypes"/>. One active rule per tenant per event type (BR-NOT-001).
    public string EventType { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int LeadDays { get; set; }

    /// <summary>A permission code, not a role — resolved to whoever currently holds it (any role, Stage 6 made roles dynamic) via <c>IUserDirectory</c>.</summary>
    public string RecipientPermission { get; set; } = string.Empty;

    /// <summary>Only meaningful for <see cref="NotificationEventTypes.ChargeOverdue"/>: how many days between re-notifications while a charge stays overdue, and the point at which <see cref="EscalationPermission"/> is additionally told. Null elsewhere.</summary>
    public int? EscalationAfterDays { get; set; }
    public string? EscalationPermission { get; set; }

    public bool IsActive { get; set; } = true;
}

/// <summary>
/// One in-app notification (FSD §23.4 "In-app notification list"): produced by the evaluation job or a save-time hook,
/// read by the recipient it names. Never edited once written, only marked read.
/// </summary>
internal class Notification : ITenantScopedEntity
{
    public int NotificationId { get; set; }
    public Guid TenantId { get; set; }
    public int UserId { get; set; }

    /// <see cref="NotificationEventTypes"/>.
    public string EventType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;

    /// <summary>What produced this notification — "Document", "VehicleRecurringChargeEntry", "VehicleRecurringCharge", "VehicleInstallment", "VehicleAttachedItem" — and its own id, for the dedup key (BR-NOT-002).</summary>
    public string SourceEntity { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    /// <summary>True for a ChargeOverdue escalation notice, sent alongside (not instead of) the ordinary recipient's own repeat notice. Part of the dedup key: the escalation recipient may be the very same person as the ordinary one, and must still get both.</summary>
    public bool IsEscalation { get; set; }

    /// <summary>Where the frontend deep-links back to — "Vehicle" or "BusinessPartner" — the same owner-type vocabulary the Documents module uses.</summary>
    public string OwnerType { get; set; } = string.Empty;
    public int OwnerId { get; set; }

    public DateTime OccurredOn { get; set; }
    public bool IsRead { get; set; }
    public DateTime? ReadOn { get; set; }
}
