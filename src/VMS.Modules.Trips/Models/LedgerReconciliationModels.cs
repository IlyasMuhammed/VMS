namespace VMS.Modules.Trips.Models;

public sealed class LedgerReconciliationRunModel
{
    public long LedgerReconciliationRunId { get; set; }
    public DateTime RunOn { get; set; }
    public int InvoicesChecked { get; set; }
    public int MismatchCount { get; set; }
    public IReadOnlyList<LedgerReconciliationMismatchModel> Mismatches { get; set; } = [];
}

public sealed class LedgerReconciliationMismatchModel
{
    public long InvoiceId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public decimal LedgerBalance { get; set; }
    public decimal InvoiceBalance { get; set; }
    public decimal Difference { get; set; }
}

/// <summary>§47.2: "POST /api/ledger-periods/{yyyymm}/close · /reopen."</summary>
public sealed class LedgerPeriodRequest
{
    public string Reason { get; set; } = string.Empty;
}

public sealed class LedgerPeriodModel
{
    public string YearMonth { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime? ClosedOn { get; set; }
    public string? CloseReason { get; set; }
    public DateTime? ReopenedOn { get; set; }
    public string? ReopenReason { get; set; }
}
