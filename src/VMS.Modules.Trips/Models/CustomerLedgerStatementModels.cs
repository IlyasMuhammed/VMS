namespace VMS.Modules.Trips.Models;

/// <summary>§40A.4's own "Customer Ledger (statement)" screen — header (opening/period/closing/overdue) plus a
/// running-balance grid, both scoped to one currency (LR-9: the ledger is kept per customer per currency, so a
/// multi-currency customer's own statement is read one currency at a time, never blended into one number).</summary>
public sealed class CustomerLedgerStatementModel
{
    public int CustomerId { get; set; }
    public string CustomerCode { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string CurrencyCode { get; set; } = string.Empty;
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public decimal OpeningBalance { get; set; }
    public decimal PeriodDebits { get; set; }
    public decimal PeriodCredits { get; set; }
    public decimal ClosingBalance { get; set; }
    public decimal OverdueAmount { get; set; }
    public IReadOnlyList<CustomerLedgerStatementRowModel> Rows { get; set; } = [];
}

public sealed class CustomerLedgerStatementRowModel
{
    public long CustomerLedgerEntryId { get; set; }
    public string EntryNumber { get; set; } = string.Empty;
    public DateOnly EntryDate { get; set; }
    public string EntryType { get; set; } = string.Empty;
    public long? InvoiceId { get; set; }
    public string? InvoiceNumber { get; set; }
    public string DocumentNo { get; set; } = string.Empty;
    public string Narration { get; set; } = string.Empty;
    public decimal DebitAmount { get; set; }
    public decimal CreditAmount { get; set; }
    public decimal RunningBalance { get; set; }
}

/// <summary>§40A.4's own "Invoice Ledger tab" — every entry for one invoice plus the reconciliation line
/// ("Ledger balance = Invoice balance").</summary>
public sealed class InvoiceLedgerModel
{
    public long InvoiceId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public decimal InvoiceBalance { get; set; }
    public decimal LedgerBalance { get; set; }
    public bool Reconciled { get; set; }
    public IReadOnlyList<LedgerEntryModel> Entries { get; set; } = [];
}

/// <summary>§40A.4's own "Customer Balances" screen row — one customer/currency, enriched with Overdue, Credit
/// and the two "last activity" dates the grid names.</summary>
public sealed class CustomerBalanceSummaryModel
{
    public int CustomerId { get; set; }
    public string CustomerCode { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string CurrencyCode { get; set; } = string.Empty;
    public decimal BalanceAmount { get; set; }
    public decimal OverdueAmount { get; set; }
    public decimal CreditAmount { get; set; }
    public DateOnly? LastPaymentDate { get; set; }
    public DateOnly? LastInvoiceDate { get; set; }
}
