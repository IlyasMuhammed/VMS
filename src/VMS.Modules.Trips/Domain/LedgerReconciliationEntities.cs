using VMS.Shared.Common;

namespace VMS.Modules.Trips.Domain;

/// <summary>§40A.5 LR-4: "A nightly reconciliation job reports any mismatch to Finance and Admin." One row per
/// run, scoped to the tenant it checked.</summary>
internal sealed class LedgerReconciliationRun : ITenantScopedEntity
{
    public long LedgerReconciliationRunId { get; set; }
    public Guid TenantId { get; set; }
    public DateTime RunOn { get; set; }
    public int InvoicesChecked { get; set; }
    public int MismatchCount { get; set; }
}

/// <summary>One Submitted, active invoice whose Σ ledger (Dr − Cr) did not equal <c>Invoice.BalanceAmount</c> —
/// LR-4's own literal comparison.</summary>
internal sealed class LedgerReconciliationMismatch : ITenantScopedEntity
{
    public long LedgerReconciliationMismatchId { get; set; }
    public Guid TenantId { get; set; }
    public long LedgerReconciliationRunId { get; set; }
    public long InvoiceId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public decimal LedgerBalance { get; set; }
    public decimal InvoiceBalance { get; set; }
    public decimal Difference { get; set; }
}

/// <summary>§40A.5 LR-7 (Recommended Design): "Finance may close a month; entries with EntryDate in a closed
/// month are rejected unless posted by Admin." One row per tenant per calendar month, only ever created when
/// that month is first closed (an absent row means "open," the default for every month that was never touched).</summary>
internal sealed class LedgerPeriod : ITenantScopedEntity
{
    public long LedgerPeriodId { get; set; }
    public Guid TenantId { get; set; }
    /// <summary><c>yyyyMM</c>, e.g. <c>"202608"</c> — matches the FSD's own literal <c>{yyyymm}</c> route segment.</summary>
    public string YearMonth { get; set; } = string.Empty;
    public string Status { get; set; } = LedgerPeriodStatuses.Open;
    public int? ClosedBy { get; set; }
    public DateTime? ClosedOn { get; set; }
    public string? CloseReason { get; set; }
    public int? ReopenedBy { get; set; }
    public DateTime? ReopenedOn { get; set; }
    public string? ReopenReason { get; set; }
}

public static class LedgerPeriodStatuses
{
    public const string Open = "Open";
    public const string Closed = "Closed";
}
