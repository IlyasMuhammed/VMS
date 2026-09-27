namespace VMS.Modules.Trips.Models;

/// <summary>One row of an Invoice List (FSD §48.5 has no numbered "Invoice List" screen of its own — every
/// invoice screen there is reached FROM one already open: Generation redirects to the invoice it just made,
/// Customer Ledger's own "row → source" opens one, a Trip's own invoice link opens one. None of those exist yet
/// for an invoice made a while ago with no ledger entry at hand, so — the same gap CC-43's own Trip Desk closed
/// for trips — there was no way to find an existing invoice at all. Needs nothing beyond the invoice's own
/// snapshot columns (<see cref="Invoice.CustomerName"/>/<see cref="Invoice.CustomerCode"/>), so, unlike
/// <see cref="TripListItem"/>, no batched cross-module lookup is needed here.</summary>
public sealed class InvoiceListItem
{
    public long InvoiceId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public int Version { get; set; }
    public int CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public DateOnly PeriodFrom { get; set; }
    public DateOnly PeriodTo { get; set; }
    public DateOnly InvoiceDate { get; set; }
    public DateOnly? DueDate { get; set; }
    public decimal NetAmount { get; set; }
    public decimal BalanceAmount { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

public sealed class InvoiceSearchFilter
{
    public int? CustomerId { get; set; }
    public string? Status { get; set; }
    public string? PaymentStatus { get; set; }
    public DateOnly? FromDate { get; set; }
    public DateOnly? ToDate { get; set; }
    public bool? IsActive { get; set; }
    public string? Search { get; set; }
}
