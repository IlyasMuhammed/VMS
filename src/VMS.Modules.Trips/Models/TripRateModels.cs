namespace VMS.Modules.Trips.Models;

public sealed class TripRateModel
{
    public long TripRateId { get; set; }
    public int CustomerId { get; set; }
    public long TripConfigurationId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public decimal RateAmount { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Remarks { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class SaveTripRateRequest
{
    public DateOnly EffectiveFrom { get; set; }
    /// <summary>Blank = open-ended (§26).</summary>
    public DateOnly? EffectiveTo { get; set; }
    public decimal RateAmount { get; set; }
    public string? CurrencyCode { get; set; }
    public string? Remarks { get; set; }
}

public sealed class UpdateTripRateRequest
{
    public decimal? RateAmount { get; set; }
    public DateOnly? EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public string? Remarks { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class InactivateTripRateRequest
{
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class SplitTripRateRequest
{
    public DateOnly Date { get; set; }
    public decimal RateAmount { get; set; }
    public string? Remarks { get; set; }
}

/// <summary>FSD §26's own rate-resolution algorithm (used later by Trip creation, CC-12), exposed here so it has
/// exactly one implementation. "Never fall back to previous, next, average or zero" — <see cref="Found"/> is false,
/// not a best guess, when nothing resolves.</summary>
public sealed class RateResolutionResult
{
    public bool Found { get; set; }
    public long? TripRateId { get; set; }
    public decimal? RateAmount { get; set; }
    public string? CurrencyCode { get; set; }
    public DateOnly? EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
}
