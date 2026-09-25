using VMS.Shared.Auditing;
using VMS.Shared.Common;

namespace VMS.Modules.Trips.Domain;

/// <summary>§40: on regeneration, 100% of every still-standing credit on the OLD invoice moves to the NEW one,
/// uncapped — original rows are never deleted. Generalizes the FSD's own literal <c>SourceInvoicePaymentId</c>
/// field (which names only a payment row) into a small discriminated <see cref="SourceKind"/>/<c>SourceId</c>
/// pair, since three different kinds of credit legitimately need to transfer: a payment (§40's own literal
/// text), an applied advance ("the applied advance transfers to the new invoice with the payments," §37.4 —
/// TASKS.md's own CC-36 scope names both explicitly), and — for a chained regeneration — a
/// prior transfer already carried onto this invoice ("the transfer carries everything v2 holds: its own payments
/// plus transfers in, each as a transfer row pointing to the source," §40's own literal "Chained regeneration"
/// paragraph, which is exactly where the FSD's own field description's "or prior transfer when chained" comes
/// from).</summary>
internal sealed class InvoicePaymentTransfer : ITenantScopedEntity, IAuditRooted
{
    public long InvoicePaymentTransferId { get; set; }
    public Guid TenantId { get; set; }
    public long OldInvoiceId { get; set; }
    public long NewInvoiceId { get; set; }

    public AuditRoot GetAuditRoot() => new("Invoice", NewInvoiceId.ToString());

    public string TransferNumber { get; set; } = string.Empty;
    public string SourceKind { get; set; } = string.Empty;
    public long SourceId { get; set; }
    public decimal AmountTransferred { get; set; }
    public DateOnly TransferDate { get; set; }
    public string Reason { get; set; } = string.Empty;
    public int TransferredBy { get; set; }
    public DateTime TransferredOn { get; set; }
    public long LedgerEntryOutId { get; set; }
    public long LedgerEntryInId { get; set; }
}

public static class PaymentTransferSourceKinds
{
    /// <summary><see cref="SourceId"/> is an <c>InvoicePaymentId</c>.</summary>
    public const string Payment = "Payment";
    /// <summary><see cref="SourceId"/> is a <c>CustomerAdvanceId</c> whose L9 <c>ADVANCE_APPLY_IN</c> credit
    /// landed on the old invoice.</summary>
    public const string AdvanceApplication = "AdvanceApplication";
    /// <summary><see cref="SourceId"/> is a prior <c>InvoicePaymentTransferId</c> — a chained regeneration
    /// (v2 → v3) carrying forward what v1 → v2 already transferred onto v2.</summary>
    public const string PriorTransfer = "PriorTransfer";
    public static readonly IReadOnlyList<string> All = [Payment, AdvanceApplication, PriorTransfer];
}

/// <summary>Backs <c>TRF-YYYY-NNNNN</c> numbering (§46.6) — the same race-safe MERGE idiom as every other
/// allocator in this module.</summary>
internal sealed class TransferNumberCounter : ITenantScopedEntity
{
    public int TransferNumberCounterId { get; set; }
    public Guid TenantId { get; set; }
    public int Year { get; set; }
    public int LastNumber { get; set; }
}
