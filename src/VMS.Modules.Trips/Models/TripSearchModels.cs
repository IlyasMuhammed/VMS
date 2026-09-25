namespace VMS.Modules.Trips.Models;

/// <summary>One row of the Trip List / Trip Desk board (FSD §48.4's unnumbered "Trip List (desk)" row): the
/// names a filter or a grid needs are resolved here, once, in one query — not left for the caller to look up
/// per row. <see cref="RouteLabel"/> is the route code for a Fixed trip, or `{From} → {To}` for an Open one;
/// intermediate stops are left out here (a list row, not the Details header) — the full label with every stop
/// is <see cref="TripModel.RouteLabel"/>, read once a specific trip is open.</summary>
public sealed class TripListItem
{
    public long TripId { get; set; }
    public string TripNumber { get; set; } = string.Empty;
    public string TripType { get; set; } = string.Empty;
    public DateOnly TripDate { get; set; }
    public int CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string? CustomerTripReference { get; set; }
    public string? RouteLabel { get; set; }
    public int VehicleId { get; set; }
    public string? VehicleRegistrationNo { get; set; }
    public int? DriverId { get; set; }
    public string? DriverName { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal? TripAmount { get; set; }
    public string? CurrencyCode { get; set; }
    public bool RateMissing { get; set; }
    /// <summary>The latest POD's own status (Uploaded/Approved/Rejected), or null when none has been uploaded
    /// yet — this list does not infer "Pending" from the customer's own POD-required setting the way the
    /// Documents/POD tab does; that reading needs the customer's billing configuration per row, a cost this
    /// board does not pay just to show one column.</summary>
    public string? PodStatus { get; set; }
    public long? InvoiceId { get; set; }
    public string? InvoiceNumber { get; set; }
    public bool IsActive { get; set; }
}

public sealed class TripSearchFilter
{
    public DateOnly? FromDate { get; set; }
    public DateOnly? ToDate { get; set; }
    public int? CustomerId { get; set; }
    public int? VehicleId { get; set; }
    public int? DriverId { get; set; }
    public string? Status { get; set; }
    public string? TripType { get; set; }
    public bool? IsActive { get; set; }
    /// <summary>true = only invoiced (InvoiceId set), false = only not-yet-invoiced, null = either.</summary>
    public bool? Invoiced { get; set; }
}
