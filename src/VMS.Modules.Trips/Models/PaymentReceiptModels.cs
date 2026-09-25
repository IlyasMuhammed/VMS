namespace VMS.Modules.Trips.Models;

public sealed class PaymentAllocationRequest
{
    public long InvoiceId { get; set; }
    public decimal Amount { get; set; }
}

/// <summary>Backs both §47.3 endpoints: <c>POST /api/invoices/{id}/payments</c> (the controller fills in a
/// single-item <see cref="Allocations"/> list and <see cref="CustomerId"/> from the invoice itself) and
/// <c>POST /api/customer-receipts</c> (the caller supplies both directly) — one request shape, one service
/// method, since the common case is simply a one-item allocation list.</summary>
public sealed class CreatePaymentReceiptRequest
{
    public int CustomerId { get; set; }
    public DateOnly ReceiptDate { get; set; }
    public decimal ReceiptAmount { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public long BankCashAccountId { get; set; }
    public string InstrumentNo { get; set; } = string.Empty;
    public DateOnly? InstrumentDate { get; set; }
    public string? DrawnOnBank { get; set; }
    public string? PaymentReference { get; set; }
    public long? AttachmentDocumentId { get; set; }
    public string? Remarks { get; set; }
    /// <summary>BR-P2: overpayment is allowed (Confirmed), but only after this confirmation.</summary>
    public bool ConfirmOverpayment { get; set; }
    /// <summary>BR-P4: proceeds anyway past the duplicate-instrument warning.</summary>
    public bool ConfirmDuplicate { get; set; }
    public List<PaymentAllocationRequest> Allocations { get; set; } = [];
    /// <summary>§37.5: "Settle remaining balance" — only meaningful with exactly one allocation (Record
    /// Payment's own single-invoice screen); a multi-invoice receipt has no single "the invoice" to settle.</summary>
    public SettleRemainingRequest? SettleRemaining { get; set; }
}

public sealed class InvoicePaymentAllocationModel
{
    public long InvoicePaymentId { get; set; }
    public long InvoiceId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public long? LedgerEntryId { get; set; }
    public decimal InvoiceBalance { get; set; }
    public string InvoicePaymentStatus { get; set; } = string.Empty;
}

public sealed class ReversePaymentRequest
{
    /// <summary>BR-P5: "reason mandatory."</summary>
    public string Reason { get; set; } = string.Empty;
    /// <summary>Defaults to today.</summary>
    public DateOnly? ReversalDate { get; set; }
}

public sealed class InvoicePaymentReversalModel
{
    public long InvoicePaymentId { get; set; }
    public long InvoiceId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public long ReversalLedgerEntryId { get; set; }
    public decimal InvoiceBalance { get; set; }
    public string InvoicePaymentStatus { get; set; } = string.Empty;
}

public sealed class CustomerReceiptModel
{
    public long CustomerReceiptId { get; set; }
    public string ReceiptNumber { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public DateOnly ReceiptDate { get; set; }
    public decimal ReceiptAmount { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = string.Empty;
    public long BankCashAccountId { get; set; }
    public string InstrumentNo { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Remarks { get; set; }
    public string RowVersion { get; set; } = string.Empty;
    public List<InvoicePaymentAllocationModel> Allocations { get; set; } = [];
    /// <summary>Set only when <see cref="CreatePaymentReceiptRequest.SettleRemaining"/> was used.</summary>
    public InvoiceSettlementModel? Settlement { get; set; }
}
