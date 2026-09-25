using VMS.Shared.Auditing;
using VMS.Shared.Common;

namespace VMS.Modules.Trips.Domain;

/// <summary>Invoice evidence (§41) — "stored, versioned document listing the trips behind an invoice," queued in
/// the same transaction as invoice creation (see <c>InvoiceCreationService.CreateAsync</c>) and rendered by a
/// background job. <see cref="EvidenceVersion"/> only increments on an Admin re-render — a Finance retry of a
/// <see cref="InvoiceEvidenceStatuses.Failed"/> row regenerates the SAME version in place, never a new one.</summary>
internal sealed class InvoiceEvidence : ITenantScopedEntity, IAuditRooted
{
    public long InvoiceEvidenceId { get; set; }
    public Guid TenantId { get; set; }
    public long InvoiceId { get; set; }

    public AuditRoot GetAuditRoot() => new("Invoice", InvoiceId.ToString());

    public int EvidenceVersion { get; set; } = 1;
    /// <summary>§41 names only one value today ("GroupBy enum: Vehicle") — stored as a string, not an int enum,
    /// the same "room for a future value, nothing to gain from an enum today" choice as <c>InvoiceLine.LineType</c>.</summary>
    public string GroupBy { get; set; } = InvoiceEvidenceGroupBy.Vehicle;
    /// <summary>Snapshot of <c>CustomerBillingConfiguration.EvidencePageSize</c> at queue time (also mirrored
    /// onto <c>Invoice.EvidencePageSize</c>, a column CC-23 already added ahead of this task).</summary>
    public int PageSize { get; set; } = 50;
    public string Status { get; set; } = InvoiceEvidenceStatuses.Queued;
    public string? ErrorMessage { get; set; }
    public int PageCount { get; set; }
    public int LineCount { get; set; }

    public string StorageKey { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;

    public int? GeneratedBy { get; set; }
    public DateTime? GeneratedAtUtc { get; set; }
}

public static class InvoiceEvidenceGroupBy
{
    public const string Vehicle = "Vehicle";
}

public static class InvoiceEvidenceStatuses
{
    public const string Queued = "Queued";
    public const string Generated = "Generated";
    public const string Failed = "Failed";
    /// <summary>§41: "Superseded when invoice becomes Inactive (file kept)" — set by the same task that inactivates
    /// an invoice (a later CC), not this one; reproduced here now so that future caller has the value ready.</summary>
    public const string Superseded = "Superseded";
}
