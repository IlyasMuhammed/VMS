namespace VMS.Modules.Trips.Models;

public sealed class TripExpenseModel
{
    public long TripExpenseId { get; set; }
    public long TripId { get; set; }
    public DateTime ExpenseDate { get; set; }
    public int ExpenseTypeId { get; set; }
    public string? OtherExpenseType { get; set; }
    public string? Description { get; set; }
    public decimal? Quantity { get; set; }
    public decimal? Rate { get; set; }
    public decimal Amount { get; set; }
    public string? Reference { get; set; }
    public int? BusinessPartnerId { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public long? AttachmentId { get; set; }
    public string ApprovalStatus { get; set; } = string.Empty;
    public string? RejectionReason { get; set; }
    public int? DecidedBy { get; set; }
    public DateTime? DecidedAtUtc { get; set; }
    public string Source { get; set; } = string.Empty;
    public bool IsVoided { get; set; }
    public string? VoidReason { get; set; }
    public int? VoidedBy { get; set; }
    public DateTime? VoidedAtUtc { get; set; }
}

public sealed class CreateTripExpenseRequest
{
    /// <summary>Defaults to now.</summary>
    public DateTime? ExpenseDate { get; set; }
    public int ExpenseTypeId { get; set; }
    public string? OtherExpenseType { get; set; }
    public string? Description { get; set; }
    public decimal? Quantity { get; set; }
    public decimal? Rate { get; set; }
    /// <summary>Defaults to Quantity × Rate when both are given; otherwise required directly.</summary>
    public decimal? Amount { get; set; }
    public string? Reference { get; set; }
    public int? BusinessPartnerId { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public long? AttachmentId { get; set; }
}

/// <summary>§47.2 lists only "approve" (no separate "reject") — this one action reaches both outcomes of §29's
/// own Pending/Approved/Rejected enum, the same one-endpoint-two-outcomes shape as invoice regeneration.</summary>
public sealed class DecideTripExpenseRequest
{
    public bool Approved { get; set; }
    /// <summary>Required when <see cref="Approved"/> is false.</summary>
    public string? Reason { get; set; }
}

public sealed class VoidTripExpenseRequest
{
    public string Reason { get; set; } = string.Empty;
}
