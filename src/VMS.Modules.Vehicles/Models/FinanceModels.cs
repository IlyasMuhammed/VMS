using VMS.Shared.Authorization;

namespace VMS.Modules.Vehicles.Models;

/// <summary>The finance block of a Draft vehicle (FSD §18.2). The schedule is generated from it when the vehicle is activated.</summary>
public class SaveFinanceRequest
{
    /// <summary>A value of the Finance Type list other than Fully Paid, which has no agreement.</summary>
    public int FinanceTypeId { get; set; }
    /// <summary>A partner holding the Bank role.</summary>
    public int BankId { get; set; }
    /// <summary>Unique for the bank.</summary>
    public string AgreementNo { get; set; } = string.Empty;
    /// <summary>Not after the acquisition date plus 90 days.</summary>
    public DateOnly? AgreementDate { get; set; }
    public decimal? FinanceAmount { get; set; }
    /// <summary>Must equal the amount paid at creation in the acquisition block (BR-VH-009).</summary>
    public decimal? DownPayment { get; set; }
    public decimal? InstallmentAmount { get; set; }
    /// <summary>Monthly, Quarterly or HalfYearly.</summary>
    public string Frequency { get; set; } = string.Empty;
    /// <summary>Number of installments, 1 to 120.</summary>
    public int? Tenure { get; set; }
    /// <summary>On or after the agreement date.</summary>
    public DateOnly? FirstDueDate { get; set; }
    /// <summary>For reference only: version 1 does not split principal and markup.</summary>
    public decimal? MarkupRate { get; set; }
    public decimal? ResidualAmount { get; set; }
    public decimal? SecurityDeposit { get; set; }
    /// <summary>Set to go ahead when the finance amount plus the down payment is not the purchase price (BR-VH-010).</summary>
    public bool ConfirmMismatch { get; set; }
    /// <summary>Required when changing an agreement that is already saved.</summary>
    public string? RowVersion { get; set; }
}

public class AgreementModel
{
    public int Id { get; set; }
    public int VehicleId { get; set; }
    public int FinanceTypeId { get; set; }
    public string? FinanceType { get; set; }
    public PartnerRef? Bank { get; set; }
    public int BankId { get; set; }
    public string AgreementNo { get; set; } = string.Empty;
    public DateOnly AgreementDate { get; set; }
    [FieldPermission(PermissionCodes.VEH_FIELD_FINANCE_VIEW)] public decimal FinanceAmount { get; set; }
    [FieldPermission(PermissionCodes.VEH_FIELD_FINANCE_VIEW)] public decimal DownPayment { get; set; }
    [FieldPermission(PermissionCodes.VEH_FIELD_FINANCE_VIEW)] public decimal InstallmentAmount { get; set; }
    public string Frequency { get; set; } = string.Empty;
    public int Tenure { get; set; }
    public DateOnly FirstDueDate { get; set; }
    public decimal? MarkupRate { get; set; }
    [FieldPermission(PermissionCodes.VEH_FIELD_FINANCE_VIEW)] public decimal? ResidualAmount { get; set; }
    [FieldPermission(PermissionCodes.VEH_FIELD_FINANCE_VIEW)] public decimal? SecurityDeposit { get; set; }
    /// <summary>Installment times tenure: what the bank is to be paid in instalments, before any residual.</summary>
    [FieldPermission(PermissionCodes.VEH_FIELD_FINANCE_VIEW)] public decimal TotalPayable { get; set; }
    public string Status { get; set; } = string.Empty;
    public string RowVersion { get; set; } = string.Empty;
}
