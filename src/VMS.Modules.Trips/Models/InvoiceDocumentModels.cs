namespace VMS.Modules.Trips.Models;

public sealed class InvoiceDocumentModel
{
    public long InvoiceDocumentId { get; set; }
    public long InvoiceId { get; set; }
    public int Version { get; set; }
    public bool IsCurrent { get; set; }
    public long SizeBytes { get; set; }
    public DateTime GeneratedAtUtc { get; set; }
    public int GeneratedBy { get; set; }
    public string Sha256 { get; set; } = string.Empty;
}

public sealed class InvoiceDocumentDownloadModel
{
    public InvoiceDocumentModel Document { get; set; } = null!;
    public string Url { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
}
