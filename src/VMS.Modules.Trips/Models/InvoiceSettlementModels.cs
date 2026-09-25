namespace VMS.Modules.Trips.Models;

public sealed class CreateSettlementRequest
{
    public string SettlementType { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    /// <summary>Defaults to today.</summary>
    public DateOnly? SettlementDate { get; set; }
    public string Reason { get; set; } = string.Empty;
}

/// <summary>§37.5: "pre-filled with the remaining amount (editable)" — offered on Record Payment when the
/// amount paid is less than the balance. <see cref="Amount"/> defaults to whatever remains after the payment
/// when omitted.</summary>
public sealed class SettleRemainingRequest
{
    public string SettlementType { get; set; } = string.Empty;
    public decimal? Amount { get; set; }
    public DateOnly? SettlementDate { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed class ReverseSettlementRequest
{
    public string Reason { get; set; } = string.Empty;
    public DateOnly? ReversalDate { get; set; }
}

public sealed class InvoiceSettlementModel
{
    public long InvoiceSettlementId { get; set; }
    public long InvoiceId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public string SettlementNumber { get; set; } = string.Empty;
    public string SettlementType { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateOnly SettlementDate { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public long? LedgerEntryId { get; set; }
    public decimal InvoiceBalance { get; set; }
    public string InvoicePaymentStatus { get; set; } = string.Empty;
}
