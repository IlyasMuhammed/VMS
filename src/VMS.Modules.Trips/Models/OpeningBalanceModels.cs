namespace VMS.Modules.Trips.Models;

/// <summary>§40A L16, LR-11: "Opening balance may be debit or credit, posted once per customer and currency at
/// go-live." <see cref="Amount"/> is signed — positive = Debit (receivable), negative = Credit — the same
/// convention every other §40A screen already reads balances with.</summary>
public sealed class PostOpeningBalanceRequest
{
    public decimal Amount { get; set; }
    public string? CurrencyCode { get; set; }
    public DateOnly AsOfDate { get; set; }
    public string? Reason { get; set; }
}

public sealed class OpeningBalanceModel
{
    public int CustomerId { get; set; }
    public string CustomerCode { get; set; } = string.Empty;
    public string CurrencyCode { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateOnly AsOfDate { get; set; }
    public string PseudoInvoiceNumber { get; set; } = string.Empty;
}

/// <summary>The importer's own one row — TASKS.md's own scope note: "CSV: customer code, amount, Dr/Cr,
/// currency, as-of date." Built as a JSON row array rather than a raw multipart CSV upload (§47.2 names no
/// separate import route at all, only the single-customer one this batches over) — a screen-side CSV parser
/// feeding this same shape achieves the identical acceptance without a second, bespoke file-parsing pipeline.</summary>
public sealed class OpeningBalanceImportRow
{
    public string CustomerCode { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? CurrencyCode { get; set; }
    public DateOnly AsOfDate { get; set; }
    public string? Reason { get; set; }
}

public sealed class ImportOpeningBalancesRequest
{
    public List<OpeningBalanceImportRow> Rows { get; set; } = [];
}

public sealed class OpeningBalanceImportRowResult
{
    public string CustomerCode { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? Error { get; set; }
}

public sealed class OpeningBalanceImportResultModel
{
    public int TotalRows { get; set; }
    public int Succeeded { get; set; }
    public int Failed { get; set; }
    public IReadOnlyList<OpeningBalanceImportRowResult> Results { get; set; } = [];
}
