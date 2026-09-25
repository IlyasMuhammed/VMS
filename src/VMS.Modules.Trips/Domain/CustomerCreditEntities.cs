using VMS.Shared.Auditing;
using VMS.Shared.Common;

namespace VMS.Modules.Trips.Domain;

/// <summary>§40's own "How Finance resolves a negative balance": moves a credit invoice's own credit (full or
/// part) to another open Submitted invoice of the same customer — L14. Neither this table nor
/// <see cref="CustomerRefund"/> gets its own document-number series (§46.6's own numbering table lists Trip,
/// Invoice, Receipt, Ledger entry, Payment transfer, Advance and Write-off/discount only — carry forward and
/// refund are deliberately absent from it), so both entities are found and shown by their own plain id, the same
/// way <see cref="InvoiceHistory"/>/<see cref="InvoiceReplacement"/> already are.</summary>
internal sealed class InvoiceCreditCarryForward : ITenantScopedEntity, IAuditRooted
{
    public long InvoiceCreditCarryForwardId { get; set; }
    public Guid TenantId { get; set; }
    public long SourceInvoiceId { get; set; }
    public long TargetInvoiceId { get; set; }

    public AuditRoot GetAuditRoot() => new("Invoice", SourceInvoiceId.ToString());

    public decimal Amount { get; set; }
    public DateOnly CarryForwardDate { get; set; }
    public string Reason { get; set; } = string.Empty;
    public int CreatedBy { get; set; }
    public DateTime CreatedOn { get; set; }
    public long LedgerEntryOutId { get; set; }
    public long LedgerEntryInId { get; set; }
}

/// <summary>§40's own "Refund" resolution of a negative invoice balance — L15's other half (the same entry type
/// also covers an un-applied advance's own refund, built by CC-34's <c>AdvanceService.RefundAsync</c> directly
/// against <c>CustomerAdvance</c>; this table is specifically the credit-invoice half of L15).</summary>
internal sealed class CustomerRefund : ITenantScopedEntity, IAuditRooted
{
    public long CustomerRefundId { get; set; }
    public Guid TenantId { get; set; }
    public long InvoiceId { get; set; }

    public AuditRoot GetAuditRoot() => new("Invoice", InvoiceId.ToString());

    public decimal Amount { get; set; }
    public DateOnly RefundDate { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public long BankCashAccountId { get; set; }
    public string? PaymentReference { get; set; }
    public string Reason { get; set; } = string.Empty;
    public int CreatedBy { get; set; }
    public DateTime CreatedOn { get; set; }
    public long LedgerEntryId { get; set; }
}
