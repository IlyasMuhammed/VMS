using VMS.Shared.Auditing;
using VMS.Shared.Common;

namespace VMS.Modules.Trips.Domain;

/// <summary>Invoice header (§32/§36/§46.4) — schema only in this task (CC-23); generation itself is CC-25's own
/// job, so most of these columns sit at their default until then. No Approved status exists (§36: "Invoice
/// approval is not required... a Generated invoice is submitted directly").</summary>
internal sealed class Invoice : ITenantScopedEntity, IAuditRooted
{
    public long InvoiceId { get; set; }
    public Guid TenantId { get; set; }

    public AuditRoot GetAuditRoot() => new("Invoice", InvoiceId.ToString());

    public string InvoiceNumber { get; set; } = string.Empty;
    public int Version { get; set; } = 1;
    /// <summary>The first invoice in a regeneration chain — itself, for one that has never been regenerated.</summary>
    public long RootInvoiceId { get; set; }
    public long? PreviousInvoiceId { get; set; }
    public long? ReplacedByInvoiceId { get; set; }

    public int CustomerId { get; set; }
    public DateOnly PeriodFrom { get; set; }
    public DateOnly PeriodTo { get; set; }
    public DateOnly InvoiceDate { get; set; }
    public DateOnly? DueDate { get; set; }

    public long? CustomerInvoiceTemplateId { get; set; }
    public int? TemplateVersion { get; set; }

    // ── Bill-to snapshot (§12/§32) — the address in force at generation, kept even if the address later
    // changes or is deactivated (CustomerBillingAddress's own doc comment names this task as where it lands). ──
    public long? CustomerBillingAddressId { get; set; }
    public string? BillToAddressName { get; set; }
    public string? BillToAddressLine1 { get; set; }
    public string? BillToAddressLine2 { get; set; }
    public int? BillToCityId { get; set; }
    public string? BillToProvinceState { get; set; }
    public int? BillToCountryId { get; set; }
    public string? BillToPostalCode { get; set; }
    public string? BillToNtn { get; set; }
    public string? BillToStrn { get; set; }

    // ── Customer snapshot (§46.4) — redundant on purpose, so a later customer edit never changes an
    // already-generated invoice. ──
    public string CustomerCode { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string? Ntn { get; set; }
    public string? Strn { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public int PaymentTermsDays { get; set; }

    public decimal TotalTripAmount { get; set; }
    public decimal TotalAdjustment { get; set; }
    public decimal GrossAmount { get; set; }
    public decimal TotalDeduction { get; set; }
    public decimal NetAmount { get; set; }

    public decimal PaidAmount { get; set; }
    public decimal AdvanceAppliedAmount { get; set; }
    public decimal WriteOffAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TransferredInAmount { get; set; }
    public decimal TransferredOutAmount { get; set; }
    public decimal CarryForwardInAmount { get; set; }
    public decimal CarryForwardOutAmount { get; set; }
    public decimal RefundedAmount { get; set; }
    public decimal BalanceAmount { get; set; }

    public string Status { get; set; } = InvoiceStatuses.Draft;
    public string PaymentStatus { get; set; } = InvoicePaymentStatuses.Unpaid;
    public bool IsActive { get; set; } = true;

    public int? GeneratedBy { get; set; }
    public DateTime? GeneratedOn { get; set; }
    public int? SubmittedBy { get; set; }
    /// <summary>The ledger date (§36: "Submit captures SubmittedOn (the ledger date)") — a business date, not a UTC instant.</summary>
    public DateOnly? SubmittedOn { get; set; }
    public string? SubmissionChannel { get; set; }
    public int? CancelledBy { get; set; }
    public DateTime? CancelledOn { get; set; }
    public string? CancelReason { get; set; }

    public string? RegenerationReason { get; set; }
    public int? RegeneratedBy { get; set; }
    public DateTime? RegeneratedOn { get; set; }

    public bool? PODRuleApplied { get; set; }
    public int? EvidencePageSize { get; set; }

    public string? Remarks { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public static class InvoiceStatuses
{
    public const string Draft = "Draft";
    public const string Generated = "Generated";
    public const string Submitted = "Submitted";
    public const string Inactive = "Inactive";
    public const string Cancelled = "Cancelled";
    public static readonly IReadOnlyList<string> All = [Draft, Generated, Submitted, Inactive, Cancelled];
}

public static class InvoicePaymentStatuses
{
    public const string Unpaid = "Unpaid";
    public const string PartiallyPaid = "PartiallyPaid";
    public const string Paid = "Paid";
    public static readonly IReadOnlyList<string> All = [Unpaid, PartiallyPaid, Paid];
}

/// <summary>§36: "submission channel (Hand / Email / Portal)."</summary>
public static class SubmissionChannels
{
    public const string Hand = "Hand";
    public const string Email = "Email";
    public const string Portal = "Portal";
    public static readonly IReadOnlyList<string> All = [Hand, Email, Portal];
}

/// <summary>One selected trip's full snapshot (§33) — genuinely append-only at the database level (§46.5: "DENY
/// UPDATE, DELETE on ... InvoiceLine to the app role"), enforced by the same <c>INSTEAD OF UPDATE, DELETE</c>
/// trigger idiom as <c>core.AuditEntries</c> (BR-BP-022's own precedent — a permission-based DENY would depend on
/// which SQL login the app happens to connect with; a trigger holds regardless). A trip's line never changes even
/// while its own invoice is still Draft — Draft editing (a later CC-25 concern) works by discarding the whole
/// draft invoice and re-generating, never by patching one line.</summary>
internal sealed class InvoiceLine : ITenantScopedEntity, IAuditRooted
{
    public long InvoiceLineId { get; set; }
    public Guid TenantId { get; set; }
    public long InvoiceId { get; set; }

    public AuditRoot GetAuditRoot() => new("Invoice", InvoiceId.ToString());

    public int LineNo { get; set; }
    /// <summary>Null for an Income line (§33: "Optional billable Trip Income lines... appear as LineType = Income").</summary>
    public long? TripId { get; set; }
    public string LineType { get; set; } = InvoiceLineTypes.Trip;

    public string? TripNumber { get; set; }
    public DateOnly? TripDate { get; set; }
    public string? TripType { get; set; }
    public string? CustomerTripReference { get; set; }
    public int? RouteId { get; set; }
    public string? RouteCode { get; set; }
    public string? RouteLabel { get; set; }
    public long? TripConfigurationId { get; set; }
    public string? TripCode { get; set; }
    public int? VehicleId { get; set; }
    public string? VehicleRegNo { get; set; }
    public int? DriverId { get; set; }
    public string? DriverName { get; set; }

    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; } = 1;
    public decimal Rate { get; set; }
    public decimal Amount { get; set; }

    public long? TripRateId { get; set; }
    public DateOnly? RateEffectiveFrom { get; set; }
    public DateOnly? RateEffectiveTo { get; set; }
    public string? RateSource { get; set; }

    public int CustomerId { get; set; }
    public string CustomerCode { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
}

public static class InvoiceLineTypes
{
    public const string Trip = "Trip";
    public const string Income = "Income";
    public static readonly IReadOnlyList<string> All = [Trip, Income];
}

/// <summary>The active billing lock of a trip (§32.1/§46.4/§46.5) — "the database-level guarantee that a trip is
/// billed on at most one active invoice." Unlinking (regeneration, a later CC-35 concern) sets
/// <see cref="IsActive"/> false and stamps <see cref="UnlinkedOn"/>/<see cref="UnlinkReason"/> rather than
/// deleting the row, so every trip's past billing history is kept.</summary>
internal sealed class InvoiceTripLink : ITenantScopedEntity, IAuditRooted
{
    public long InvoiceTripLinkId { get; set; }
    public Guid TenantId { get; set; }
    public long InvoiceId { get; set; }
    public long TripId { get; set; }
    public long InvoiceLineId { get; set; }

    public AuditRoot GetAuditRoot() => new("Invoice", InvoiceId.ToString());

    public bool IsActive { get; set; } = true;
    public DateTime LinkedOn { get; set; }
    public DateTime? UnlinkedOn { get; set; }
    public string? UnlinkReason { get; set; }
}

/// <summary>§32.3: exceptional corrections for a closed period, added to the invoice being generated now without
/// touching the old, already-closed invoice.</summary>
internal sealed class InvoiceAdjustment : ITenantScopedEntity, IAuditRooted
{
    public long InvoiceAdjustmentId { get; set; }
    public Guid TenantId { get; set; }
    public long InvoiceId { get; set; }

    public AuditRoot GetAuditRoot() => new("Invoice", InvoiceId.ToString());

    public string AdjustmentMonth { get; set; } = string.Empty;
    public decimal AdjustmentAmount { get; set; }
    public string AdjustmentNote { get; set; } = string.Empty;
    public string? ReferenceInvoiceNo { get; set; }
    public int Sequence { get; set; }
}

/// <summary>The deduction snapshot (§14/§35) — what a customer's tax/deduction rule actually charged on this
/// specific invoice, kept even if the rule itself is later superseded or its amount changes.</summary>
internal sealed class InvoiceTaxLine : ITenantScopedEntity, IAuditRooted
{
    public long InvoiceTaxLineId { get; set; }
    public Guid TenantId { get; set; }
    public long InvoiceId { get; set; }
    public long? CustomerTaxRuleId { get; set; }

    public AuditRoot GetAuditRoot() => new("Invoice", InvoiceId.ToString());

    public string TaxName { get; set; } = string.Empty;
    public string TaxCode { get; set; } = string.Empty;
    public string TaxType { get; set; } = string.Empty;
    public decimal? TaxPercentage { get; set; }
    public decimal? FixedAmount { get; set; }
    public string CalculationBasis { get; set; } = string.Empty;
    public int Sequence { get; set; }
    /// <summary>Snapshotted from <c>CustomerTaxRule.Applicable</c> at generation time (§35: "non-applicable rules
    /// are still snapshotted with DeductionAmount 0 for traceability") — distinguishes "not applicable" from
    /// "applicable but computed to zero" (a negative or zero basis), which <see cref="Amount"/> alone cannot.</summary>
    public bool Applicable { get; set; }
    public decimal Amount { get; set; }
}

/// <summary>Status transitions with reason (§36/§46.2) — Draft→Generated→Submitted and the two terminal states,
/// each its own row, never edited.</summary>
internal sealed class InvoiceHistory : ITenantScopedEntity, IAuditRooted
{
    public long InvoiceHistoryId { get; set; }
    public Guid TenantId { get; set; }
    public long InvoiceId { get; set; }

    public AuditRoot GetAuditRoot() => new("Invoice", InvoiceId.ToString());

    public string FromStatus { get; set; } = string.Empty;
    public string ToStatus { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public int ChangedBy { get; set; }
    public DateTime ChangedAtUtc { get; set; }
}

/// <summary>Many-old-to-one-new replacement map for overlap regeneration (§39/§46.2) — several superseded
/// invoices can each point at the one invoice that replaced them.</summary>
internal sealed class InvoiceReplacement : ITenantScopedEntity, IAuditRooted
{
    public long InvoiceReplacementId { get; set; }
    public Guid TenantId { get; set; }
    public long OldInvoiceId { get; set; }
    public long NewInvoiceId { get; set; }

    public AuditRoot GetAuditRoot() => new("Invoice", OldInvoiceId.ToString());

    public string? Reason { get; set; }
    public int CreatedBy { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
