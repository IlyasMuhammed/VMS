using VMS.Shared.Auditing;
using VMS.Shared.Common;

namespace VMS.Modules.Trips.Domain;

/// <summary>Additional operational revenue attached to a trip (§30) — detention, an extra drop, loading recovery.
/// The trip's own <c>TripAmount</c> stays the primary revenue line; this is only ever an extra.</summary>
internal sealed class TripIncome : ITenantScopedEntity, IAuditRooted
{
    public long TripIncomeId { get; set; }
    public Guid TenantId { get; set; }
    public long TripId { get; set; }

    public AuditRoot GetAuditRoot() => new("Trip", TripId.ToString());

    public int CustomerId { get; set; }
    public int IncomeTypeId { get; set; }
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public DateOnly IncomeDate { get; set; }
    /// <summary>§30: "If 1, included on the trip's invoice as an extra line with LineType = Income."</summary>
    public bool IsBillable { get; set; }
    /// <summary>Set once the not-yet-built invoicing task bills this row. "A billed income row cannot be edited" —
    /// there is no edit action at all yet (§47.2 lists only GET/POST for Income), so the one place this guard
    /// bites today is <c>TripIncomeService.VoidAsync</c>, this task's own reasoned addition mirroring Fuel/Expense.</summary>
    public long? InvoiceLineId { get; set; }
    public string? Reference { get; set; }
    public string? Remarks { get; set; }

    public bool IsVoided { get; set; }
    public string? VoidReason { get; set; }
    public int? VoidedBy { get; set; }
    public DateTime? VoidedAtUtc { get; set; }
}
