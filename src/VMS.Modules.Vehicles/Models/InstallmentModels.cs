using VMS.Shared.Authorization;

namespace VMS.Modules.Vehicles.Models;

/// <summary>A payment against one installment of the schedule (source §8). It may be less than what is expected: the rest stays due.</summary>
public class PayInstallmentRequest
{
    public decimal? Amount { get; set; }
    /// <summary>The day it was paid. Defaults to today; not in the future and not before the vehicle was acquired.</summary>
    public DateOnly? PaidOn { get; set; }
    /// <summary>Cash, BankTransfer, Cheque or PayOrder.</summary>
    public string? PaymentMode { get; set; }
    public string? Reference { get; set; }
    /// <summary>The installment's row version, so two payments entered at once cannot together pay more than is due.</summary>
    public string RowVersion { get; set; } = string.Empty;
}

public class InstallmentModel
{
    public int Id { get; set; }
    public int AgreementId { get; set; }
    public int InstallmentNo { get; set; }
    public DateOnly DueDate { get; set; }
    [FieldPermission(PermissionCodes.VEH_FIELD_FINANCE_VIEW)] public decimal ExpectedAmount { get; set; }
    [FieldPermission(PermissionCodes.VEH_FIELD_FINANCE_VIEW)] public decimal PaidAmount { get; set; }
    /// <summary>What is still to pay on this installment.</summary>
    [FieldPermission(PermissionCodes.VEH_FIELD_FINANCE_VIEW)] public decimal RemainingAmount { get; set; }
    public DateOnly? PaidOn { get; set; }
    public bool IsResidual { get; set; }
    public string Status { get; set; } = string.Empty;
    public string RowVersion { get; set; } = string.Empty;
}

/// <summary>The payment recorded, and whether it settled the agreement.</summary>
public class InstallmentPaymentModel
{
    public InstallmentModel Installment { get; set; } = new();
    /// <summary>The ledger entry the payment posted.</summary>
    public int TransactionId { get; set; }
    /// <summary>True when this payment was the last that was due, so the agreement is now settled.</summary>
    public bool AgreementSettled { get; set; }
}
