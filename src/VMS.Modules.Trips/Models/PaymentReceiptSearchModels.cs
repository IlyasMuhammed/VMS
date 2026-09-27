namespace VMS.Modules.Trips.Models;

/// <summary>Receipts List (FSD §48.5's own unnumbered row: "All receipts... Grid: receipt no., date, customer,
/// method, instrument, amount, invoices, status") — a screen the FSD names explicitly, but with no matching
/// endpoint: <see cref="Services.IPaymentReceiptService"/> only ever had <c>CreateAsync</c> before this task.
/// The same gap CC-43/44 have now hit for Trips and Invoices, closed the same way.</summary>
public sealed class ReceiptListItem
{
    public long CustomerReceiptId { get; set; }
    public string ReceiptNumber { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public DateOnly ReceiptDate { get; set; }
    public decimal ReceiptAmount { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = string.Empty;
    public long BankCashAccountId { get; set; }
    public string InstrumentNo { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public IReadOnlyList<string> InvoiceNumbers { get; set; } = [];
}

public sealed class ReceiptSearchFilter
{
    public int? CustomerId { get; set; }
    public string? PaymentMethod { get; set; }
    public long? BankCashAccountId { get; set; }
    public string? Status { get; set; }
    public DateOnly? FromDate { get; set; }
    public DateOnly? ToDate { get; set; }
}
