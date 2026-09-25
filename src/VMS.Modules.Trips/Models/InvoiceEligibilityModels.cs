namespace VMS.Modules.Trips.Models;

public sealed class EligibleTripsQuery
{
    public int CustomerId { get; set; }
    public DateOnly PeriodFrom { get; set; }
    public DateOnly PeriodTo { get; set; }
    /// <summary>Defaults to today (§32's own "InvoiceDate ... Default today").</summary>
    public DateOnly? InvoiceDate { get; set; }
    /// <summary>When searching to regenerate an invoice, that invoice's own trip links are treated as available
    /// for this search only (§32.1 rule 4) rather than "Already Invoiced.")</summary>
    public long? RegeneratingInvoiceId { get; set; }
}

public static class EligibleTripCategories
{
    public const string Available = "Available";
    public const string AlreadyInvoiced = "AlreadyInvoiced";
    public const string Blocked = "Blocked";
}

/// <summary>§32.1's own blocking reason codes — matching the FSD's own literal example (<c>RATE_MISSING</c>)
/// and inventing reasonably-named siblings for the rest, since the FSD names only that one explicitly.</summary>
public static class TripBlockingCodes
{
    public const string RateMissing = "RATE_MISSING";
    public const string PodMissing = "POD_MISSING";
    public const string Inactive = "INACTIVE";
    public const string NotCompleted = "NOT_COMPLETED";
    public const string CurrencyMismatch = "CURRENCY_MISMATCH";
}

public sealed class EligibleTripsSummary
{
    public int CompletedTrips { get; set; }
    public int AlreadyInvoiced { get; set; }
    public int Blocked { get; set; }
    public int Available { get; set; }
    public decimal AvailableAmount { get; set; }
}

public sealed class EligibleTripRow
{
    public long TripId { get; set; }
    public string TripNumber { get; set; } = string.Empty;
    public DateOnly TripDate { get; set; }
    public string Category { get; set; } = string.Empty;
    public decimal? Amount { get; set; }
    public string? Vehicle { get; set; }
    public string? Route { get; set; }
    public string? CustomerTripReference { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class TripBlockingError
{
    public string Code { get; set; } = string.Empty;
    public long TripId { get; set; }
    public DateOnly TripDate { get; set; }
    public string? Configuration { get; set; }
}

public sealed class InvoiceOverlapRow
{
    public long InvoiceId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateOnly PeriodFrom { get; set; }
    public DateOnly PeriodTo { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public bool FullyPaid { get; set; }
}

public sealed class InvoiceTemplateOption
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Version { get; set; }
    public bool IsDefault { get; set; }
}

public sealed class InvoiceAddressOption
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
}

public sealed class TaxRulePreview
{
    public string TaxCode { get; set; } = string.Empty;
    public decimal? Rate { get; set; }
    public string CalculationBasis { get; set; } = string.Empty;
}

public sealed class ResolvedInvoiceOptions
{
    public IReadOnlyList<InvoiceTemplateOption> TemplateOptions { get; set; } = [];
    public IReadOnlyList<InvoiceAddressOption> BillingAddressOptions { get; set; } = [];
    public IReadOnlyList<TaxRulePreview> TaxRulesPreview { get; set; } = [];
}

/// <summary>§47.3's own literal response shape for <c>POST /api/invoices/search-eligible-trips</c>.</summary>
public sealed class EligibleTripsResult
{
    public EligibleTripsSummary Summary { get; set; } = new();
    public IReadOnlyList<EligibleTripRow> Trips { get; set; } = [];
    public IReadOnlyList<TripBlockingError> BlockingErrors { get; set; } = [];
    public IReadOnlyList<InvoiceOverlapRow> Overlaps { get; set; } = [];
    public ResolvedInvoiceOptions Resolved { get; set; } = new();
}
