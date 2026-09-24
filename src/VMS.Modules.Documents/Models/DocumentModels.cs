namespace VMS.Modules.Documents.Models;

/// <summary>One version of a document (FSD §23A.3), current or superseded.</summary>
public class DocumentModel
{
    public int Id { get; set; }
    public string DocumentCode { get; set; } = string.Empty;
    public string OwnerType { get; set; } = string.Empty;
    public int OwnerId { get; set; }
    public int DocumentTypeId { get; set; }
    public string? DocumentTypeName { get; set; }
    public int VersionNo { get; set; }
    public bool IsCurrent { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? DocumentNumber { get; set; }
    public string? Provider { get; set; }
    public DateOnly? IssueDate { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    /// <summary>Days until expiry; negative once expired. Null when the type is not expirable.</summary>
    public int? DaysRemaining { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string? RejectReason { get; set; }
    public int? LinkedTransactionId { get; set; }
    public DateTime CreatedOn { get; set; }
}

/// <summary>One document slot: its current version (if any) plus, on request, its history.</summary>
public class DocumentSlotModel
{
    public int DocumentTypeId { get; set; }
    public string DocumentTypeName { get; set; } = string.Empty;
    public string MandatoryLevel { get; set; } = string.Empty;
    public bool HasCost { get; set; }
    public DocumentModel? Current { get; set; }
    public List<DocumentModel> History { get; set; } = [];
}

public class UploadDocumentRequest
{
    public int? DocumentTypeId { get; set; }
    public string? DocumentNumber { get; set; }
    public string? Provider { get; set; }
    public string? IssueDate { get; set; }
    public string? ExpiryDate { get; set; }
}

/// <summary>Pre-filled from the current version by the client (BR-DOC-005); the server computes the same suggested expiry and accepts an override.</summary>
public class RenewDocumentRequest
{
    public string? DocumentNumber { get; set; }
    public string? Provider { get; set; }
    public string? IssueDate { get; set; }
    public string? ExpiryDate { get; set; }
    /// <summary>
    /// BR-DOC-006: the ledger transaction the client already posted for this premium or fee by confirming the matching recurring
    /// charge entry (the one, sole posting path — this field never posts anything itself, only tags the new version with what paid
    /// for it, for traceability). Left null when the type has no cost or nothing was linked.
    /// </summary>
    public int? LinkedTransactionId { get; set; }
}

public class RejectDocumentRequest
{
    public string? Reason { get; set; }
}

public class DownloadLinkModel
{
    public string Url { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
}
