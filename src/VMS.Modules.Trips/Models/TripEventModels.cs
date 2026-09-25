namespace VMS.Modules.Trips.Models;

public sealed class TripEventModel
{
    public long TripEventId { get; set; }
    public long TripId { get; set; }
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
    public string Source { get; set; } = string.Empty;
    public Guid? ClientEventId { get; set; }
}

/// <summary>The manual "Add Event" action (§24's own screen table: "type, time, location, odometer, remarks,
/// attachment"). <see cref="EventType"/> is restricted server-side to <c>TripEventTypes.ManuallyCreatable</c>.</summary>
public sealed class CreateTripEventRequest
{
    public string EventType { get; set; } = string.Empty;
    /// <summary>Defaults to now. §25: "≤ now + 10 min" — a small clock-skew allowance, not "never future."</summary>
    public DateTime? EventDateTime { get; set; }
    public int? CityId { get; set; }
    public string? LocationText { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public decimal? Odometer { get; set; }
    public string? Remarks { get; set; }
    public long? AttachmentId { get; set; }
    /// <summary>Which channel logged it. Restricted to Manual/DriverApp here — GPS is reserved for Phase 2 (§25)
    /// and System is only ever used by this module's own automatic event-writing, never a caller.</summary>
    public string? Source { get; set; }
    /// <summary>AC-54: the offline driver app's own idempotency key. A retried sync with the same id returns the
    /// original event unchanged rather than creating a duplicate.</summary>
    public Guid? ClientEventId { get; set; }
}
