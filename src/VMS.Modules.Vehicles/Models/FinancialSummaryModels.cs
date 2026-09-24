using VMS.Shared.Authorization;

namespace VMS.Modules.Vehicles.Models;

/// <summary>
/// A vehicle's money, worked out from its ledger and its installment schedule each time it is asked for (BR-VH-003). Nothing here is
/// stored on the vehicle, so it can never disagree with the entries it is made of.
/// </summary>
public class FinancialSummary
{
    public int VehicleId { get; set; }

    /// <summary>What the vehicle cost to acquire: the acquisition entries.</summary>
    [FieldPermission(PermissionCodes.VEH_FIELD_COST_VIEW)] public decimal AcquisitionCost { get; set; }
    /// <summary>Registration and the cost of attached items.</summary>
    [FieldPermission(PermissionCodes.VEH_FIELD_COST_VIEW)] public decimal MajorExpenses { get; set; }
    /// <summary>Acquisition cost plus major expenses.</summary>
    [FieldPermission(PermissionCodes.VEH_FIELD_COST_VIEW)] public decimal TotalCost { get; set; }
    /// <summary>The initial payment plus every installment paid (§19). With a purchase of 5,000,000 of which 2,000,000 was paid, it is 2,000,000; after an installment of 150,000, 2,150,000.</summary>
    [FieldPermission(PermissionCodes.VEH_FIELD_COST_VIEW)] public decimal PaidToDate { get; set; }
    /// <summary>Security deposits held by others for this vehicle.</summary>
    [FieldPermission(PermissionCodes.VEH_FIELD_COST_VIEW)] public decimal Deposits { get; set; }

    /// <summary>The bank agreement in force (or the latest one), if the vehicle has one.</summary>
    public AgreementSummary? Agreement { get; set; }
}

public class AgreementSummary
{
    public int AgreementId { get; set; }
    public string Status { get; set; } = string.Empty;
    public PartnerRef? Bank { get; set; }
    /// <summary>Installment times tenure: what is to be paid in installments, before any residual.</summary>
    [FieldPermission(PermissionCodes.VEH_FIELD_FINANCE_VIEW)] public decimal TotalPayable { get; set; }
    [FieldPermission(PermissionCodes.VEH_FIELD_FINANCE_VIEW)] public decimal Residual { get; set; }
    /// <summary>Total payable plus residual, less what has been paid on the schedule (FR-VH-006). Zero once settled.</summary>
    [FieldPermission(PermissionCodes.VEH_FIELD_FINANCE_VIEW)] public decimal Outstanding { get; set; }
    public int InstallmentsPaid { get; set; }
    public int InstallmentsTotal { get; set; }
    /// <summary>The earliest installment not yet paid in full.</summary>
    public DateOnly? NextDueDate { get; set; }
    [FieldPermission(PermissionCodes.VEH_FIELD_FINANCE_VIEW)] public decimal? NextDueAmount { get; set; }
    /// <summary>Installments past their due date and not paid in full.</summary>
    public int OverdueCount { get; set; }
    [FieldPermission(PermissionCodes.VEH_FIELD_FINANCE_VIEW)] public decimal OverdueAmount { get; set; }
}
