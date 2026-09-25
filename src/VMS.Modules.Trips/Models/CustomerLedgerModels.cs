namespace VMS.Modules.Trips.Models;

public sealed class LedgerEntryModel
{
    public long CustomerLedgerEntryId { get; set; }
    public string EntryNumber { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public long? InvoiceId { get; set; }
    public long? TripId { get; set; }
    public string EntryType { get; set; } = string.Empty;
    public DateOnly EntryDate { get; set; }
    public decimal DebitAmount { get; set; }
    public decimal CreditAmount { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public string SourceType { get; set; } = string.Empty;
    public long SourceId { get; set; }
    public long? ReversesEntryId { get; set; }
    public string DocumentNo { get; set; } = string.Empty;
    public string Narration { get; set; } = string.Empty;
    public long CustomerSeq { get; set; }
}

public sealed class CustomerBalanceModel
{
    public int CustomerId { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public decimal BalanceAmount { get; set; }
}
