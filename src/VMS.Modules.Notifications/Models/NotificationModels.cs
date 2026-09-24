namespace VMS.Modules.Notifications.Models;

/// <summary>The notification rule master (FSD §9), for the admin screen (S7-NOT-05).</summary>
public class NotificationRuleModel
{
    public int Id { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int LeadDays { get; set; }
    public string RecipientPermission { get; set; } = string.Empty;
    /// <summary>Only set for the ChargeOverdue rule.</summary>
    public int? EscalationAfterDays { get; set; }
    public string? EscalationPermission { get; set; }
    public bool IsActive { get; set; }
}

/// <summary>Only the lead days, recipient, escalation and active flag are editable — the event type and name are fixed (BR-NOT-001).</summary>
public class SaveNotificationRuleRequest
{
    public int? LeadDays { get; set; }
    public string? RecipientPermission { get; set; }
    public int? EscalationAfterDays { get; set; }
    public string? EscalationPermission { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>One row of the in-app notification list (S7-NOT-06).</summary>
public class NotificationModel
{
    public int Id { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string OwnerType { get; set; } = string.Empty;
    public int OwnerId { get; set; }
    public bool IsEscalation { get; set; }
    public DateTime OccurredOn { get; set; }
    public bool IsRead { get; set; }
}

public class UnreadCountModel
{
    public int Count { get; set; }
}
