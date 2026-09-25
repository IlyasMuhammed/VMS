using VMS.Shared.Auditing;
using VMS.Shared.Common;

namespace VMS.Modules.Trips.Domain;

/// <summary>§37: the money received (one cheque, one bank transfer, one cash deposit) — the header a
/// <see cref="InvoicePayment"/> allocation is created against. Payments are never edited or deleted (BR-P5);
/// a wrong one is reversed (CC-32's own job — <see cref="Status"/> only ever becomes
/// <see cref="CustomerReceiptStatuses.Reversed"/> there, never here).</summary>
internal sealed class CustomerReceipt : ITenantScopedEntity, IAuditRooted
{
    public long CustomerReceiptId { get; set; }
    public Guid TenantId { get; set; }

    public AuditRoot GetAuditRoot() => new("Customer", CustomerId.ToString());

    public string ReceiptNumber { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public string ReceiptType { get; set; } = ReceiptTypes.InvoicePayment;
    public DateOnly ReceiptDate { get; set; }
    public decimal ReceiptAmount { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = string.Empty;
    public long BankCashAccountId { get; set; }
    public string InstrumentNo { get; set; } = string.Empty;
    public DateOnly? InstrumentDate { get; set; }
    public string? DrawnOnBank { get; set; }
    public string? PaymentReference { get; set; }
    /// <summary>Accepted per §37's own field list but not validated against the Documents module — the same
    /// deliberately minimal choice CC-29 made for Submit's own AcknowledgementDocumentId; add real validation
    /// only once a caller actually needs it enforced.</summary>
    public long? AttachmentDocumentId { get; set; }
    public string Status { get; set; } = CustomerReceiptStatuses.Posted;
    public string? Remarks { get; set; }
    public int CreatedBy { get; set; }
    public DateTime CreatedOn { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public static class ReceiptTypes
{
    public const string InvoicePayment = "InvoicePayment";
    /// <summary>§37.4 (CC-34's own job) — money received against an Open trip before any invoice exists.</summary>
    public const string Advance = "Advance";
}

public static class CustomerReceiptStatuses
{
    public const string Posted = "Posted";
    public const string Reversed = "Reversed";
}

public static class PaymentMethods
{
    /// <summary>AC-63: "the method list shows only Direct to Account and Bank Cheque" — Phase 1 has no Cash
    /// method at all, despite the header table's own generic "BankCashAccount" name.</summary>
    public const string DirectToAccount = "DirectToAccount";
    public const string BankCheque = "BankCheque";
    public static readonly IReadOnlyList<string> All = [DirectToAccount, BankCheque];
}

/// <summary>§37: one allocation of a <see cref="CustomerReceipt"/> to one invoice. The common case (one receipt,
/// one invoice) is still exactly one of these rows — Record Payment is not a different code path, just a
/// one-item allocation list.</summary>
internal sealed class InvoicePayment : ITenantScopedEntity, IAuditRooted
{
    public long InvoicePaymentId { get; set; }
    public Guid TenantId { get; set; }
    public long CustomerReceiptId { get; set; }
    public long InvoiceId { get; set; }

    public AuditRoot GetAuditRoot() => new("Invoice", InvoiceId.ToString());

    public DateOnly PaymentDate { get; set; }
    public decimal Amount { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public string? PaymentReference { get; set; }
    public long BankCashAccountId { get; set; }
    public long? LedgerEntryId { get; set; }
    public string Status { get; set; } = InvoicePaymentRowStatuses.Posted;
    public string? Remarks { get; set; }
    public int CreatedBy { get; set; }
    public DateTime CreatedOn { get; set; }

    // §37 BR-P5 (CC-32): "reason mandatory" for a reversal — kept as its own field, not folded into Remarks,
    // so the ORIGINAL entry reason (if any) and the reversal's own reason never overwrite each other.
    public string? ReversalReason { get; set; }
    public int? ReversedBy { get; set; }
    public DateTime? ReversedOn { get; set; }
}

/// <summary>The allocation ROW's own status — distinct from <c>Invoice.PaymentStatus</c> (Unpaid/PartiallyPaid/
/// Paid), which is the invoice's own derived summary, not this row's lifecycle.</summary>
public static class InvoicePaymentRowStatuses
{
    public const string Posted = "Posted";
    public const string Reversed = "Reversed";
    /// <summary>§37: "Transferred = moved to a regenerated invoice; still counts historically" — CC-36's own job.</summary>
    public const string Transferred = "Transferred";
}
