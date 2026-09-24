namespace VMS.Shared.Notifications;

/// <summary>
/// The events the notification rule master governs (FSD §19A.6, §23.4). One rule per event type, admin-configurable —
/// "all lead times are values in the notification rule master, not constants in code" (§23.4). Lives in Shared, not in the
/// Notifications module itself, because the Documents and Vehicles modules' own <see cref="IDocumentNotificationSource"/>
/// and <see cref="IVehicleNotificationSource"/> implementations need to tag their candidates with these without taking a
/// dependency on the Notifications module.
/// </summary>
public static class NotificationEventTypes
{
    /// <summary>A current document with an expiry date is approaching it.</summary>
    public const string DocumentExpiry = "DocumentExpiry";
    /// <summary>A recurring charge entry has become Due.</summary>
    public const string ChargeDue = "ChargeDue";
    /// <summary>A recurring charge entry is Overdue — repeats on the rule's escalation cadence while it stays so.</summary>
    public const string ChargeOverdue = "ChargeOverdue";
    /// <summary>A recurring charge's own schedule is ending (its <c>EndDate</c> is approaching).</summary>
    public const string ChargeEnding = "ChargeEnding";
    /// <summary>A bank lease installment is approaching its due date.</summary>
    public const string InstallmentDue = "InstallmentDue";
    /// <summary>An attached item's warranty is approaching its end.</summary>
    public const string ItemWarrantyEnd = "ItemWarrantyEnd";

    public static readonly IReadOnlyList<string> All = [DocumentExpiry, ChargeDue, ChargeOverdue, ChargeEnding, InstallmentDue, ItemWarrantyEnd];
}

/// <summary>
/// One candidate the notification evaluator considers, from a source module's own data — no rule applied yet, no recipient
/// resolved yet, just "here is a date something is watching". <see cref="SourceEntity"/> and <see cref="SourceId"/> together
/// are the dedup key's other half (with the recipient and event type), so an already-renewed or already-paid record silently
/// stops producing new candidates the moment its underlying row disappears or its id changes, rather than needing a "resolved" flag.
/// </summary>
public sealed record NotificationCandidate(
    string EventType, string SourceEntity, string SourceId, string OwnerType, int OwnerId, string OwnerName, DateOnly TriggerDate, string Summary);

/// <summary>
/// What the Notifications module asks the Documents module for — every current document whose expiry is worth watching —
/// without reading the Documents module's own tables. Implemented by the Documents module; only the caller's own tenant's documents are ever returned.
/// </summary>
public interface IDocumentNotificationSource
{
    /// <summary>Every current, live-status document with an expiry date set (FSD §23.4's four DocumentExpiry rows all reduce to this one query).</summary>
    Task<IReadOnlyList<NotificationCandidate>> FindDueAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// What the Notifications module asks the Vehicles module for — charge entries, charge schedules, installments and attached
/// items worth watching — without reading the Vehicles module's own tables. Implemented by the Vehicles module; only the
/// caller's own tenant's vehicles are ever returned.
/// </summary>
public interface IVehicleNotificationSource
{
    /// <summary>Every candidate across all five vehicle-side event types (ChargeDue, ChargeOverdue, ChargeEnding, InstallmentDue, ItemWarrantyEnd), tagged by its own <c>EventType</c>.</summary>
    Task<IReadOnlyList<NotificationCandidate>> FindDueAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// What a source module calls right after saving a record the notification rules watch (FR-BP-016, FR-VH-008): "a
/// document expiring in 10 days is picked up immediately", not left to wait for the next hourly run. Implemented by the
/// Notifications module; a caller with nothing registered (a test host that never added the Notifications module) gets a
/// harmless no-op rather than a startup failure, since this is a courtesy call, never the record's own save transaction.
/// </summary>
public interface INotificationTrigger
{
    /// <summary>Fire-and-continue: evaluates only the one just-saved record's candidates against the tenant's rules. Never throws for the caller's sake — a notification miss is not worth failing the save that triggered it.</summary>
    Task NotifyAsync(string sourceEntity, string sourceId, CancellationToken cancellationToken = default);
}

/// <summary>The default when no module has registered the real thing (a test host built without the Notifications module) — every save-time hook still has something to call.</summary>
public sealed class NoNotificationTrigger : INotificationTrigger
{
    public Task NotifyAsync(string sourceEntity, string sourceId, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
