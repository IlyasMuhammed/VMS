using VMS.Shared.Auditing;
using VMS.Shared.Common;

namespace VMS.Modules.Trips.Domain;

/// <summary>§37.5: settling part of an invoice as a Write-off or a Discount — "the only discretionary credits
/// (Confirmed); free-form manual journals are not available" (LR-8). Never edited or deleted; a wrong one is
/// reversed (reason mandatory), the same append-only discipline as <see cref="InvoicePayment"/>.</summary>
internal sealed class InvoiceSettlement : ITenantScopedEntity, IAuditRooted
{
    public long InvoiceSettlementId { get; set; }
    public Guid TenantId { get; set; }
    public long InvoiceId { get; set; }

    public AuditRoot GetAuditRoot() => new("Invoice", InvoiceId.ToString());

    public string SettlementNumber { get; set; } = string.Empty;
    public string SettlementType { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateOnly SettlementDate { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string Status { get; set; } = InvoiceSettlementStatuses.Posted;
    public long? LedgerEntryId { get; set; }
    public int CreatedBy { get; set; }
    public DateTime CreatedOn { get; set; }

    public string? ReversalReason { get; set; }
    public int? ReversedBy { get; set; }
    public DateTime? ReversedOn { get; set; }
}

public static class SettlementTypes
{
    public const string WriteOff = "WriteOff";
    public const string Discount = "Discount";
    public static readonly IReadOnlyList<string> All = [WriteOff, Discount];
}

public static class InvoiceSettlementStatuses
{
    public const string Posted = "Posted";
    public const string Reversed = "Reversed";
}
