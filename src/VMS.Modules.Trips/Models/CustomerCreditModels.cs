namespace VMS.Modules.Trips.Models;

/// <summary>§40's own modal body: target invoice (from the customer's own open Submitted invoices) and amount —
/// "Amount cannot exceed the available credit" is this module's own literal validation error.</summary>
public sealed class CarryForwardRequest
{
    public long TargetInvoiceId { get; set; }
    public decimal Amount { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed class CarryForwardModel
{
    public long InvoiceCreditCarryForwardId { get; set; }
    public long SourceInvoiceId { get; set; }
    public string SourceInvoiceNumber { get; set; } = string.Empty;
    public decimal SourceInvoiceBalance { get; set; }
    public long TargetInvoiceId { get; set; }
    public string TargetInvoiceNumber { get; set; } = string.Empty;
    public decimal TargetInvoiceBalance { get; set; }
    public decimal Amount { get; set; }
    public string Reason { get; set; } = string.Empty;
}

/// <summary>§40's own modal body: date, method, account, reference, amount.</summary>
public sealed class RefundCreditRequest
{
    public DateOnly? RefundDate { get; set; }
    public decimal Amount { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public long BankCashAccountId { get; set; }
    public string? Reference { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed class CustomerRefundModel
{
    public long CustomerRefundId { get; set; }
    public long InvoiceId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateOnly RefundDate { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public decimal InvoiceBalance { get; set; }
    public string Reason { get; set; } = string.Empty;
}
