namespace VMS.Modules.Trips.Models;

public sealed class TripDocumentModel
{
    public long TripDocumentId { get; set; }
    public long TripId { get; set; }
    public string DocumentType { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public int UploadedBy { get; set; }
    public DateTime UploadedAtUtc { get; set; }
    public string Source { get; set; } = string.Empty;
}

public sealed class TripPODModel
{
    public long TripPODId { get; set; }
    public long TripId { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public int UploadedBy { get; set; }
    public DateTime UploadedAtUtc { get; set; }
    public string Source { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int? ApprovedBy { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public string? RejectedReason { get; set; }
}

public sealed class RejectPodRequest
{
    public string Reason { get; set; } = string.Empty;
}

/// <summary>Module-local, same shape as the Documents module's own <c>DownloadLinkModel</c> — not shared between
/// them since neither owns the other and the shape is small enough that duplicating it beats a cross-module
/// reference for something this trivial.</summary>
public sealed class TripDownloadLinkModel
{
    public string Url { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
}
