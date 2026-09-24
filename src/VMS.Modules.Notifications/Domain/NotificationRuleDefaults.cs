using VMS.Shared.Authorization;
using VMS.Shared.Notifications;

namespace VMS.Modules.Notifications.Domain;

/// <summary>One row of a tenant's starting notification rule master (FSD §19A.6, §23.4). Values every tenant starts with; Admin may edit the lead days and recipients under Administration (S7-NOT-05).</summary>
public sealed record NotificationRuleSeed(string EventType, string Name, int LeadDays, string RecipientPermission, int? EscalationAfterDays, string? EscalationPermission);

/// <summary>
/// The six event types' starting rules, checked against the FSD's own two tables by a test (the same discipline as
/// S0-FND-13's lookup seeds and S5-DOC-01's document type seeds): §19A.6 gives the charge-side leads (3 days before due,
/// 1 day after then every 3 days for overdue, 30 days before a schedule ends); §23.4 gives the document/installment/item
/// leads folded in here as <see cref="NotificationEventTypes.DocumentExpiry"/> (30 days, the commonest of its four rows —
/// insurance, fitness, route permit, token tax all share it) and the rest at their own table values. "Rent due day
/// (Rented)" from §23.4 is not a rule of its own: Vehicle Rent Payable is itself a recurring charge (Stage 4), so it is
/// already covered by <see cref="NotificationEventTypes.ChargeDue"/>.
/// </summary>
public static class NotificationRuleDefaults
{
    public static readonly IReadOnlyList<NotificationRuleSeed> Defaults =
    [
        new(NotificationEventTypes.DocumentExpiry,  "Document expiry",           30, PermissionCodes.DOC_REGISTER_VIEW, null, null),
        new(NotificationEventTypes.ChargeDue,        "Charge entry due",          3,  PermissionCodes.FIN_DUE_CONFIRM, null, null),
        new(NotificationEventTypes.ChargeOverdue,    "Charge entry overdue",      1,  PermissionCodes.FIN_DUE_CONFIRM, 3,    PermissionCodes.ADM_NOTIFICATION_MANAGE),
        new(NotificationEventTypes.ChargeEnding,     "Charge schedule ending",    30, PermissionCodes.FIN_RECURRING_MANAGE, null, null),
        new(NotificationEventTypes.InstallmentDue,   "Bank installment due",      3,  PermissionCodes.VEH_FIELD_FINANCE_VIEW, null, null),
        new(NotificationEventTypes.ItemWarrantyEnd,  "Attached item warranty end", 15, PermissionCodes.VEH_VIEW, null, null),
    ];
}
