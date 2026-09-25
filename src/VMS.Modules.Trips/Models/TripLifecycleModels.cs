namespace VMS.Modules.Trips.Models;

public sealed class TransitionTripRequest
{
    /// <summary>Required for the transition into Started (§21: "Start odometer captured").</summary>
    public decimal? StartOdometer { get; set; }
    /// <summary>Required for the transition into Delivered (§24: "End odometer").</summary>
    public decimal? EndOdometer { get; set; }
    public string RowVersion { get; set; } = string.Empty;
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
