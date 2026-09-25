namespace VMS.Modules.Trips.Models;

/// <summary>§31: <c>Trip P&amp;L = (TripAmount + Approved Trip Income) - (Fuel + Approved Trip Expenses)</c>.
/// Income has no approval workflow of its own (§30's field table has no ApprovalStatus) — "Approved Trip Income"
/// is read here as every non-voided income row, the closest honest match to the FSD's own wording.</summary>
public sealed class TripPnLModel
{
    public long TripId { get; set; }
    public string TripNumber { get; set; } = string.Empty;
    /// <summary>False when the trip's own rate never resolved (§26/§31: "shown as 'Not priced'"). Fuel/Income/
    /// Expenses are still totalled either way; only <see cref="OperationalPnL"/> stays null.</summary>
    public bool IsPriced { get; set; }
    public decimal? Revenue { get; set; }
    public decimal ApprovedIncome { get; set; }
    public decimal Fuel { get; set; }
    public decimal ApprovedExpenses { get; set; }
    public decimal? OperationalPnL { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
}

public sealed class TripPnLQuery
{
    public int? CustomerId { get; set; }
    public int? VehicleId { get; set; }
    public DateOnly? FromDate { get; set; }
    public DateOnly? ToDate { get; set; }
}

/// <summary>§31: "Trips with RateMissing ... are excluded from totals with a count shown" — this is the
/// concretely-testable shape of that rule (PNL-08's own full report screen, with its by-vehicle/driver/customer/
/// route/month aggregations, is a separate later Reports task; this is the underlying calculation it will read).</summary>
public sealed class TripPnLSummaryModel
{
    public IReadOnlyList<TripPnLModel> Trips { get; set; } = [];
    public decimal TotalRevenue { get; set; }
    public decimal TotalIncome { get; set; }
    public decimal TotalFuel { get; set; }
    public decimal TotalExpenses { get; set; }
    public decimal TotalPnL { get; set; }
    public int PricedTripCount { get; set; }
    public int UnpricedTripCount { get; set; }
}
