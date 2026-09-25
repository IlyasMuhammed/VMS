namespace VMS.Modules.Trips.Models;

public sealed class InvoiceEvidenceModel
{
    public long InvoiceEvidenceId { get; set; }
    public long InvoiceId { get; set; }
    public int EvidenceVersion { get; set; }
    public string GroupBy { get; set; } = string.Empty;
    public int PageSize { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public int PageCount { get; set; }
    public int LineCount { get; set; }
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public int? GeneratedBy { get; set; }
    public DateTime? GeneratedAtUtc { get; set; }
    public List<InvoiceEvidenceVehiclePageModel> VehiclePages { get; set; } = [];
}

/// <summary>AC-33's own worked shape — "pages 1 and 2 both belong to ABC-123, ABC-456 starts on page 3" —
/// exposed as data so it's provable without parsing the rendered PDF's bytes.</summary>
public sealed class InvoiceEvidenceVehiclePageModel
{
    public string VehicleRegNo { get; set; } = string.Empty;
    public int FirstPage { get; set; }
    public int LastPage { get; set; }
    public int LineCount { get; set; }
    public decimal Subtotal { get; set; }
}

public sealed class InvoiceEvidenceDownloadModel
{
    public InvoiceEvidenceModel Evidence { get; set; } = null!;
    public string Url { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
}
