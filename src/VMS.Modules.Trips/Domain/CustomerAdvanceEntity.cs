using VMS.Shared.Auditing;
using VMS.Shared.Common;

namespace VMS.Modules.Trips.Domain;

/// <summary>§37.4: an advance payment against an Open trip, applied automatically when the trip's own invoice is
/// Submitted. Backed by a <see cref="CustomerReceipt"/> the same shape a payment uses (method/account/instrument
/// fields), just with <see cref="CustomerReceipt.ReceiptType"/> = <see cref="ReceiptTypes.Advance"/>.</summary>
internal sealed class CustomerAdvance : ITenantScopedEntity, IAuditRooted
{
    public long CustomerAdvanceId { get; set; }
    public Guid TenantId { get; set; }
    public string AdvanceNumber { get; set; } = string.Empty;
    public long CustomerReceiptId { get; set; }
    public int CustomerId { get; set; }
    public long TripId { get; set; }

    public AuditRoot GetAuditRoot() => new("Trip", TripId.ToString());

    public DateOnly AdvanceDate { get; set; }
    public decimal Amount { get; set; }
    public decimal AppliedAmount { get; set; }
    public decimal RefundedAmount { get; set; }
    public string Status { get; set; } = CustomerAdvanceStatuses.Open;
    public long? LedgerEntryId { get; set; }
    public int CreatedBy { get; set; }
    public DateTime CreatedOn { get; set; }

    // §37.4: "Finance can move the advance to another Open trip (reason, audited)" — the move itself just
    // repoints TripId (a plain mutable column, unlike the append-only ledger); these record the most recent one.
    public long? MovedFromTripId { get; set; }
    public string? MoveReason { get; set; }
    public int? MovedBy { get; set; }
    public DateTime? MovedOn { get; set; }

    public string? RefundReason { get; set; }
    public int? RefundedBy { get; set; }
    public DateTime? RefundedOn { get; set; }
    public long? RefundLedgerEntryId { get; set; }

    public string? ReversalReason { get; set; }
    public int? ReversedBy { get; set; }
    public DateTime? ReversedOn { get; set; }
}

public static class CustomerAdvanceStatuses
{
    public const string Open = "Open";
    public const string Applied = "Applied";
    public const string Refunded = "Refunded";
    public const string Reversed = "Reversed";
}
