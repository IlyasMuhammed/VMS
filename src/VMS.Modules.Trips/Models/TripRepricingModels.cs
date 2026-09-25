namespace VMS.Modules.Trips.Models;

public sealed class ResolveMissingRatesRequest
{
    public int? CustomerId { get; set; }
    public long? TripConfigurationId { get; set; }
}

public sealed class ResolveMissingRatesResult
{
    public int Considered { get; set; }
    public int Updated { get; set; }
    public int StillMissing { get; set; }
    public List<long> UpdatedTripIds { get; set; } = [];
}

public sealed class RepriceTripsRequest
{
    public List<long> TripIds { get; set; } = [];
    /// <summary>Required when <see cref="Commit"/> is true (§26: "with ... a reason"); ignored for a preview.</summary>
    public string? Reason { get; set; }
    /// <summary>False (the default) returns the preview only, with nothing written. True commits it.</summary>
    public bool Commit { get; set; }
}

public sealed class RepriceResultItem
{
    public long TripId { get; set; }
    public string TripNumber { get; set; } = string.Empty;
    public decimal? OldAmount { get; set; }
    public string OldRateSource { get; set; } = string.Empty;
    public decimal? NewAmount { get; set; }
    public string NewRateSource { get; set; } = string.Empty;
    /// <summary>Excluded (already on an active invoice, or an Open trip with a manual amount rather than a rate
    /// master entry) — never re-resolved even in preview, so its Old/New are identical and this explains why.</summary>
    public bool Excluded { get; set; }
    public string? ExcludedReason { get; set; }
}

public sealed class RepriceResult
{
    public bool Committed { get; set; }
    public List<RepriceResultItem> Items { get; set; } = [];
}
