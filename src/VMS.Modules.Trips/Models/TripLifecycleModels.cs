namespace VMS.Modules.Trips.Models;

public sealed class TransitionTripRequest
{
    /// <summary>Required for the transition into Started (§21: "Start odometer captured").</summary>
    public decimal? StartOdometer { get; set; }
    /// <summary>Required for the transition into Delivered (§24: "End odometer").</summary>
    public decimal? EndOdometer { get; set; }
    public string RowVersion { get; set; } = string.Empty;
    /// <summary>§43/AC-54: "the driver taps Delivered while offline... the event syncs once... and is not
    /// duplicated on retry" — a retried sync with the same id is answered with the trip's own current state
    /// instead of re-attempting (and likely failing) the same transition a second time. Reuses the exact
    /// <see cref="Domain.TripEvent.ClientEventId"/> column CC-16 already built for manual events, rather than a
    /// second dedup mechanism just for status steps.</summary>
    public Guid? ClientEventId { get; set; }
}

public sealed class HoldTripRequest
{
    public string Reason { get; set; } = string.Empty;
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class ResumeTripRequest
{
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class CancelTripRequest
{
    public string Reason { get; set; } = string.Empty;
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class ReopenTripRequest
{
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class ChangeTripActiveRequest
{
    /// <summary>Required to inactivate; not read for reactivate.</summary>
    public string? Reason { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}
