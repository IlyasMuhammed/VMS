using VMS.Shared.Auditing;
using VMS.Shared.Common;

namespace VMS.Modules.Trips.Domain;

/// <summary>§40A: the Customer Ledger (accounts receivable sub-ledger). Append-only — enforced by the same
/// <c>INSTEAD OF UPDATE, DELETE</c> trigger idiom <c>InvoiceLine</c> (CC-23) and <c>core.AuditEntries</c> already
/// use, since "no UPDATE/DELETE grants for the application role" depends on which login connects, while a trigger
/// holds regardless. CC-30 only ever writes L1-L3 (submission) and L11 (cancellation mirror); every other L-code
/// in <see cref="LedgerEntryTypes"/> is reproduced now (matching this schema's own "build the whole enum surface
/// once" convention) for CC-31..40 to post into later, unreachable until then.</summary>
internal sealed class CustomerLedgerEntry : ITenantScopedEntity, IAuditRooted
{
    public long CustomerLedgerEntryId { get; set; }
    public Guid TenantId { get; set; }

    public AuditRoot GetAuditRoot() => new("Customer", CustomerId.ToString());

    public string EntryNumber { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public long? InvoiceId { get; set; }
    public long? TripId { get; set; }
    public string EntryType { get; set; } = string.Empty;
    /// <summary>Submission date for L1-L3 (§40A.2, Confirmed) — a business date, not a UTC instant.</summary>
    public DateOnly EntryDate { get; set; }
    public DateTime PostedOn { get; set; }
    public decimal DebitAmount { get; set; }
    public decimal CreditAmount { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public string SourceType { get; set; } = string.Empty;
    public long SourceId { get; set; }
    public long? ReversesEntryId { get; set; }
    public string DocumentNo { get; set; } = string.Empty;
    public string Narration { get; set; } = string.Empty;
    /// <summary>Monotonic per customer (§40A.2: "for stable running balance order") — allocated by
    /// <see cref="ICustomerLedgerSequenceAllocator"/>, a separate counter from <see cref="EntryNumber"/>'s own
    /// tenant-wide-per-year one.</summary>
    public long CustomerSeq { get; set; }
    public bool IsSystemGenerated { get; set; } = true;
    public int CreatedBy { get; set; }
}

/// <summary>§40A.1's own L1-L16 posting-rule table, reproduced in full.</summary>
public static class LedgerEntryTypes
{
    public const string Invoice = "INVOICE";
    public const string Adjustment = "ADJUSTMENT";
    public const string Deduction = "DEDUCTION";
    public const string Payment = "PAYMENT";
    public const string PaymentReversal = "PAYMENT_REVERSAL";
    public const string WriteOff = "WRITE_OFF";
    public const string Discount = "DISCOUNT";
    public const string SettlementReversal = "SETTLEMENT_REVERSAL";
    public const string Advance = "ADVANCE";
    public const string AdvanceApplyOut = "ADVANCE_APPLY_OUT";
    public const string AdvanceApplyIn = "ADVANCE_APPLY_IN";
    public const string AdvanceReversal = "ADVANCE_REVERSAL";
    public const string InvoiceCancel = "INVOICE_CANCEL";
    public const string InvoiceSuperseded = "INVOICE_SUPERSEDED";
    public const string TransferOut = "TRANSFER_OUT";
    public const string TransferIn = "TRANSFER_IN";
    public const string CarryForwardOut = "CARRY_FORWARD_OUT";
    public const string CarryForwardIn = "CARRY_FORWARD_IN";
    public const string Refund = "REFUND";
    public const string OpeningBalance = "OPENING_BALANCE";
}

/// <summary>§40A.2's own <c>SourceType</c> enum.</summary>
public static class LedgerSourceTypes
{
    public const string Invoice = "Invoice";
    public const string InvoiceAdjustment = "InvoiceAdjustment";
    public const string InvoiceTaxLine = "InvoiceTaxLine";
    public const string InvoicePayment = "InvoicePayment";
    public const string InvoiceSettlement = "InvoiceSettlement";
    public const string CustomerAdvance = "CustomerAdvance";
    public const string PaymentTransfer = "PaymentTransfer";
    public const string CarryForward = "CarryForward";
    public const string Refund = "Refund";
    public const string OpeningBalance = "OpeningBalance";
}

/// <summary>§40A.2: "CustomerBalance table ... updated in the same transaction for fast lookups; the ledger rows
/// remain the source of truth." Kept per customer per currency (LR-9), not one row per customer.</summary>
internal sealed class CustomerBalance : ITenantScopedEntity
{
    public long CustomerBalanceId { get; set; }
    public Guid TenantId { get; set; }
    public int CustomerId { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public decimal BalanceAmount { get; set; }
    public long? LastEntryId { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
