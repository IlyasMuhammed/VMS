using VMS.Shared.Authorization;

namespace VMS.Modules.Vehicles.Models;

/// <summary>One row of a vehicle's configured recurring charges (FSD §19A.1), current or historic.</summary>
public class RecurringChargeModel
{
    public int Id { get; set; }
    public Guid SeriesId { get; set; }
    public int VehicleId { get; set; }
    public int ChargeTypeId { get; set; }
    public string? ChargeType { get; set; }
    public PartnerRef? Payee { get; set; }
    public int ExpenseTypeId { get; set; }
    public string? ExpenseType { get; set; }
    [FieldPermission(PermissionCodes.VEH_FIELD_COST_VIEW)] public decimal? Amount { get; set; }
    public string AmountBasis { get; set; } = string.Empty;
    public string Frequency { get; set; } = string.Empty;
    public int? DueDay { get; set; }
    public int? DueMonth { get; set; }
    public int? CustomIntervalDays { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public int? OccurrenceCount { get; set; }
    public int GeneratedCount { get; set; }
    public string PostingMode { get; set; } = string.Empty;
    public int GenerateLeadDays { get; set; }
    [FieldPermission(PermissionCodes.VEH_FIELD_COST_VIEW)] public decimal? TaxWithholdingPercent { get; set; }
    public DateOnly? NextDueDate { get; set; }
    /// <summary>True while this is the version in force; false for a superseded or ended row kept as history (BR-VH-033).</summary>
    public bool IsActive { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public string? EndReason { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

/// <summary>Configures a new charge, or the terms of an amendment (saved as a new effective-dated row, BR-VH-033).</summary>
public class SaveRecurringChargeRequest
{
    public int? ChargeTypeId { get; set; }
    public int? PayeeId { get; set; }
    public int? ExpenseTypeId { get; set; }
    public decimal? Amount { get; set; }
    public string? AmountBasis { get; set; }
    public string? Frequency { get; set; }
    public int? DueDay { get; set; }
    public int? DueMonth { get; set; }
    public int? CustomIntervalDays { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public int? OccurrenceCount { get; set; }
    public string? PostingMode { get; set; }
    public int? GenerateLeadDays { get; set; }
    public decimal? TaxWithholdingPercent { get; set; }
    /// <summary>Required when amending: the row being replaced, so a change made by someone else in the meantime is not overwritten.</summary>
    public string? RowVersion { get; set; }
}

/// <summary>Ending a charge by hand, outside an amendment (BR-VH-033 §19A.5 "End-date").</summary>
public class EndRecurringChargeRequest
{
    public DateOnly? EndDate { get; set; }
    public string? Reason { get; set; }
}

/// <summary>One generated occurrence (FSD §19A.4), or a Bank Installment surfaced from the finance schedule (BR-VH-029) shown the same way.</summary>
public class PayableModel
{
    public int Id { get; set; }
    /// <summary><c>RecurringCharge</c> or <c>Installment</c>: which kind of due item this is, and so which action confirms it.</summary>
    public string Kind { get; set; } = string.Empty;
    public int VehicleId { get; set; }
    public string? VehicleRegistrationNo { get; set; }
    public Guid? BranchId { get; set; }
    public string? Branch { get; set; }
    public int? ChargeId { get; set; }
    public int? ChargeTypeId { get; set; }
    public string? ChargeType { get; set; }
    public PartnerRef? Payee { get; set; }
    public DateOnly DueDate { get; set; }
    [FieldPermission(PermissionCodes.VEH_FIELD_COST_VIEW)] public decimal? ExpectedAmount { get; set; }
    public string Status { get; set; } = string.Empty;
}

/// <summary>Confirms one entry: the actual amount and date, which may differ from what was expected (BR-VH-031).</summary>
public class ConfirmChargeEntryRequest
{
    public decimal? Amount { get; set; }
    public DateOnly? PaidOn { get; set; }
    public string? PaymentMode { get; set; }
    public string? Reference { get; set; }
    public string? Remarks { get; set; }
}

public class WaiveChargeEntryRequest
{
    public string? Reason { get; set; }
}

/// <summary>What confirming an entry wrote: the entry itself, refreshed, and the ledger entry it posted.</summary>
public class ChargeEntryPaymentModel
{
    public int TransactionId { get; set; }
}

/// <summary>Bulk-confirms a batch of due items from the Payables Due workbench with one payment date and mode (§19A.5, FR-VH-016). Mixed kinds are fine: each is routed to the action that kind uses one by one.</summary>
public class BulkConfirmRequest
{
    public List<BulkConfirmItem> Items { get; set; } = [];
    public DateOnly? PaidOn { get; set; }
    public string? PaymentMode { get; set; }
}

public class BulkConfirmItem
{
    public string Kind { get; set; } = string.Empty;
    public int VehicleId { get; set; }
    public int Id { get; set; }
    /// <summary>For an Installment item: its row version, the same guard <c>PayInstallment</c> takes one at a time.</summary>
    public string? RowVersion { get; set; }
    /// <summary>Overrides the batch's amount for this one item, if it differs.</summary>
    public decimal? Amount { get; set; }
}

public class BulkConfirmResult
{
    public int Succeeded { get; set; }
    public List<BulkConfirmFailure> Failed { get; set; } = [];
}

public class BulkConfirmFailure
{
    public string Kind { get; set; } = string.Empty;
    public int VehicleId { get; set; }
    public int Id { get; set; }
    public string Message { get; set; } = string.Empty;
}

/// <summary>The dashboard tile (§19A.5): what is due soon and what is already overdue, fleet-wide.</summary>
public class PayablesSummaryModel
{
    public int DueWithin7Days { get; set; }
    [FieldPermission(PermissionCodes.VEH_FIELD_COST_VIEW)] public decimal DueWithin7DaysAmount { get; set; }
    public int OverdueCount { get; set; }
    [FieldPermission(PermissionCodes.VEH_FIELD_COST_VIEW)] public decimal OverdueAmount { get; set; }
}
