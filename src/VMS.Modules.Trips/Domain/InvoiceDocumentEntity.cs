using VMS.Shared.Auditing;
using VMS.Shared.Common;

namespace VMS.Modules.Trips.Domain;

/// <summary>A rendered invoice PDF (§34) — "Re-print returns the stored file; 'Re-render' is Admin-only and
/// creates a new document version without altering data." One row per version; the current one is
/// <see cref="IsCurrent"/>, never overwritten in place.</summary>
internal sealed class InvoiceDocument : ITenantScopedEntity, IAuditRooted
{
    public long InvoiceDocumentId { get; set; }
    public Guid TenantId { get; set; }
    public long InvoiceId { get; set; }

    public AuditRoot GetAuditRoot() => new("Invoice", InvoiceId.ToString());

    public string DocumentType { get; set; } = InvoiceDocumentTypes.InvoicePdf;
    public int Version { get; set; } = 1;
    public string StorageKey { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public int GeneratedBy { get; set; }
    public DateTime GeneratedAtUtc { get; set; }
    public bool IsCurrent { get; set; } = true;
}

public static class InvoiceDocumentTypes
{
    public const string InvoicePdf = "InvoicePDF";
}
