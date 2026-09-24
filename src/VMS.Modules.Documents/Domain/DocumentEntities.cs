using VMS.Shared.Auditing;
using VMS.Shared.Common;

namespace VMS.Modules.Documents.Domain;

/// <summary>
/// The document type master (FSD §23A.1), per tenant, lazy-seeded from the platform's twelve defaults (§23A.2) the same way a
/// lookup list is (S0-FND-12's idiom): a tenant starts with none and gets the seed the first time its types are asked for.
/// </summary>
internal class DocumentType : ITenantScopedEntity
{
    public int DocumentTypeId { get; set; }
    public Guid TenantId { get; set; }

    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public AppliesTo AppliesTo { get; set; }
    /// <summary>Narrows a Business Partner type to one role's partners (a Driving Licence is only offered for the Driver role). Null = every partner.</summary>
    public string? PartnerRole { get; set; }

    public bool IsExpirable { get; set; }
    public int? DefaultValidityValue { get; set; }
    public string? DefaultValidityUnit { get; set; }
    public bool IsPeriodic { get; set; }
    public int RenewalLeadDays { get; set; } = 30;
    /// <see cref="MandatoryLevels"/>.
    public string MandatoryLevel { get; set; } = MandatoryLevels.None;
    public bool RequiresDocumentNumber { get; set; }
    public bool HasCost { get; set; }
    /// <summary>A mask of <c>VMS.Shared.Files.FileKinds</c>.</summary>
    public int AllowedFormats { get; set; }
    public int MaxFileSizeMb { get; set; } = 10;
    public int RetentionYears { get; set; } = 7;

    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}

/// <summary>
/// One version of a document (FSD §23A.3): a slot (owner + type) holds a current version and a history of superseded ones, never
/// replaced in place (BR-DOC-001). Never physically deleted by a user; only the retention job removes a superseded or rejected
/// version once its type's retention period has passed (BR-DOC-002), and logs what it removed.
/// </summary>
internal class Document : ITenantScopedEntity, IAuditRooted
{
    public int DocumentId { get; set; }
    public Guid TenantId { get; set; }

    /// <summary>Root-audited under the owner, the same way a vehicle's child rows are — a partner's or vehicle's History tab shows this too.</summary>
    public AuditRoot GetAuditRoot() => new(OwnerType, OwnerId.ToString());

    public string DocumentCode { get; set; } = string.Empty;
    /// <see cref="DocumentOwnerTypes"/>.
    public string OwnerType { get; set; } = string.Empty;
    public int OwnerId { get; set; }
    public int DocumentTypeId { get; set; }

    /// <summary>1 for the first upload of a slot, incrementing on each renewal.</summary>
    public int VersionNo { get; set; }
    /// <summary>True for the one version of a slot that is current. A filtered unique index allows at most one per owner and type.</summary>
    public bool IsCurrent { get; set; }
    /// <see cref="DocumentStatuses"/>.
    public string Status { get; set; } = DocumentStatuses.Active;

    public string? DocumentNumber { get; set; }
    public string? Provider { get; set; }
    public DateOnly? IssueDate { get; set; }
    public DateOnly? ExpiryDate { get; set; }

    public string StorageKey { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public long SizeBytes { get; set; }

    public string? RejectReason { get; set; }
    /// <summary>The ledger entry a renewal's cost posted to, or that it was linked to instead of posting a new one (BR-DOC-006), so the payment is never recorded twice.</summary>
    public int? LinkedTransactionId { get; set; }

    public int CreatedBy { get; set; }
    public DateTime CreatedOn { get; set; }
    [NotAudited] public int? ModifiedBy { get; set; }
    [NotAudited] public DateTime ModifiedOn { get; set; }
}
