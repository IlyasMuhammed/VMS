using VMS.Shared.Auditing;
using VMS.Shared.Common;

namespace VMS.Modules.Trips.Domain;

/// <summary>Every operational step is a Trip Event (FSD §25) — the trip's own timeline. Status transitions create
/// these automatically (<c>TripLifecycleService</c>/<c>TripService.SaveNewTripAsync</c> call <c>ITripEventRecorder</c>
/// directly, in the same transaction as the change they record); this task's own manual "Add Event", POD upload
/// and Issue flows create the rest. Append-only by convention (§25: "corrections are made by a new Note/correction
/// event," never an edit) — nothing in this module updates or deletes a row here once written.</summary>
internal sealed class TripEvent : ITenantScopedEntity, IAuditRooted
{
    public long TripEventId { get; set; }
    public Guid TenantId { get; set; }
    public long TripId { get; set; }

    public AuditRoot GetAuditRoot() => new("Trip", TripId.ToString());

    public string EventType { get; set; } = string.Empty;
    public DateTime EventDateTime { get; set; }
    public int? CityId { get; set; }
    public string? LocationText { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public decimal? Odometer { get; set; }
    public int UserId { get; set; }
    public string? Remarks { get; set; }
    public long? AttachmentId { get; set; }
    public string Source { get; set; } = TripEventSources.Manual;
    /// <summary>§25/AC-54: idempotency for the offline driver app — a retried sync with the same id never
    /// duplicates the event. Null for events this system creates itself (status transitions, etc.), which have
    /// no client to retry them.</summary>
    public Guid? ClientEventId { get; set; }
}

public static class TripEventTypes
{
    public const string Created = "Created";
    public const string Planned = "Planned";
    public const string Assigned = "Assigned";
    public const string Started = "Started";
    public const string InTransit = "InTransit";
    public const string ArrivedPickup = "ArrivedPickup";
    public const string LoadingComplete = "LoadingComplete";
    public const string ArrivedDelivery = "ArrivedDelivery";
    public const string Delivered = "Delivered";
    public const string PODUploaded = "PODUploaded";
    public const string Completed = "Completed";
    public const string OnHold = "OnHold";
    public const string Resumed = "Resumed";
    public const string Cancelled = "Cancelled";
    public const string Fuel = "Fuel";
    public const string Expense = "Expense";
    public const string Issue = "Issue";
    public const string Note = "Note";
    public const string StatusSkipped = "StatusSkipped";

    public static readonly IReadOnlyList<string> All =
    [
        Created, Planned, Assigned, Started, InTransit, ArrivedPickup, LoadingComplete, ArrivedDelivery, Delivered,
        PODUploaded, Completed, OnHold, Resumed, Cancelled, Fuel, Expense, Issue, Note, StatusSkipped
    ];

    /// <summary>What a caller may create directly through the generic "Add Event" action (§24's screen table).
    /// Every other type in <see cref="All"/> is produced automatically by its own dedicated action (a status
    /// transition, an issue, a POD upload, or — not yet built — fuel/expense), so a manual duplicate of those
    /// would desync the timeline from what actually happened.</summary>
    public static readonly IReadOnlyList<string> ManuallyCreatable = [Note];

    /// <summary>The one status-to-event mapping §25's own enum needs (its names differ slightly from
    /// <see cref="TripStatuses"/>'s: AtPickup→ArrivedPickup, Loaded→LoadingComplete, AtDelivery→ArrivedDelivery).</summary>
    public static string ForStatus(string status) => status switch
    {
        TripStatuses.Planned => Planned,
        TripStatuses.Assigned => Assigned,
        TripStatuses.Started => Started,
        TripStatuses.InTransit => InTransit,
        TripStatuses.AtPickup => ArrivedPickup,
        TripStatuses.Loaded => LoadingComplete,
        TripStatuses.AtDelivery => ArrivedDelivery,
        TripStatuses.Delivered => Delivered,
        TripStatuses.Completed => Completed,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "No trip event type maps to this status.")
    };
}

public static class TripEventSources
{
    public const string Manual = "Manual";
    public const string DriverApp = "DriverApp";
    public const string Gps = "GPS";
    public const string System = "System";
}
