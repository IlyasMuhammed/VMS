namespace VMS.Modules.Trips.Models;

public sealed class CreateAdvanceRequest
{
    public DateOnly AdvanceDate { get; set; }
    public decimal Amount { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public long BankCashAccountId { get; set; }
    public string InstrumentNo { get; set; } = string.Empty;
    public DateOnly? InstrumentDate { get; set; }
    public string? DrawnOnBank { get; set; }
    public string? PaymentReference { get; set; }
    public string? Remarks { get; set; }
}

public sealed class MoveAdvanceRequest
{
    public long ToTripId { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed class RefundAdvanceRequest
{
    public string Reason { get; set; } = string.Empty;
    public DateOnly? RefundDate { get; set; }
}

public sealed class ReverseAdvanceRequest
{
    public string Reason { get; set; } = string.Empty;
    public DateOnly? ReversalDate { get; set; }
}

public sealed class CustomerAdvanceModel
{
    public long CustomerAdvanceId { get; set; }
    public string AdvanceNumber { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public long TripId { get; set; }
    public string TripNumber { get; set; } = string.Empty;
    public DateOnly AdvanceDate { get; set; }
    public decimal Amount { get; set; }
    public decimal AppliedAmount { get; set; }
    public decimal RefundedAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public long? LedgerEntryId { get; set; }
}
