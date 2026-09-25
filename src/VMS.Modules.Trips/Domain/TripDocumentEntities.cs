using VMS.Shared.Auditing;
using VMS.Shared.Common;

namespace VMS.Modules.Trips.Domain;

/// <summary>A trip attachment that is not a POD (§23: loading slip, gate pass, challan, photo, other) — POD has
/// its own table and workflow (<see cref="TripPOD"/>), since it alone carries an approval status the FSD gives no
/// other trip attachment type.</summary>
internal sealed class TripDocument : ITenantScopedEntity, IAuditRooted
{
    public long TripDocumentId { get; set; }
    public Guid TenantId { get; set; }
    public long TripId { get; set; }

    public AuditRoot GetAuditRoot() => new("Trip", TripId.ToString());

    public string DocumentType { get; set; } = TripDocumentTypes.Other;
    public string StorageKey { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public int UploadedBy { get; set; }
    public DateTime UploadedAtUtc { get; set; }
    public string Source { get; set; } = TripEventSources.Manual;
}

public static class TripDocumentTypes
{
    public const string LoadingSlip = "LoadingSlip";
    public const string GatePass = "GatePass";
    public const string Challan = "Challan";
    public const string Photo = "Photo";
    public const string Other = "Other";
    public static readonly IReadOnlyList<string> All = [LoadingSlip, GatePass, Challan, Photo, Other];
}

/// <summary>Proof of delivery (§23/§25) — its own table, not a <see cref="TripDocument"/> row, because it alone
/// carries the approval workflow <see cref="PodStatuses"/> describes: "approval by Operations when the customer
/// requires POD." One row per upload attempt, so a Rejected POD's history stays alongside whatever replaces it.</summary>
internal sealed class TripPOD : ITenantScopedEntity, IAuditRooted
{
    public long TripPODId { get; set; }
    public Guid TenantId { get; set; }
    public long TripId { get; set; }

    public AuditRoot GetAuditRoot() => new("Trip", TripId.ToString());

    public string StorageKey { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public int UploadedBy { get; set; }
    public DateTime UploadedAtUtc { get; set; }
    public string Source { get; set; } = TripEventSources.Manual;

    public string Status { get; set; } = PodStatuses.Uploaded;
    public int? ApprovedBy { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public string? RejectedReason { get; set; }
}

/// <summary>§23's own four values. "Pending" is never stored as a row — it is the computed state of a trip whose
/// customer requires POD and has no <see cref="TripPOD"/> row at all yet; a row only ever starts at Uploaded.</summary>
public static class PodStatuses
{
    public const string Uploaded = "Uploaded";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
}

/// <summary>§23's own six issue types: "Breakdown, Accident, Delay, Customer Hold, Route Blocked, Other; with
/// severity, description, photo, resolved flag. An issue can put the trip On Hold" (the reporter's own choice —
/// see <c>CreateTripIssueRequest.PutOnHold</c>, since the FSD names no rule for which types always do).</summary>
internal sealed class TripIssue : ITenantScopedEntity, IAuditRooted
{
    public long TripIssueId { get; set; }
    public Guid TenantId { get; set; }
    public long TripId { get; set; }

    public AuditRoot GetAuditRoot() => new("Trip", TripId.ToString());

    public string IssueType { get; set; } = TripIssueTypes.Other;
    public string Severity { get; set; } = IssueSeverities.Medium;
    public string Description { get; set; } = string.Empty;
    /// <summary>An already-uploaded <see cref="TripDocument"/> (DocumentType Photo) — reuses that upload path
    /// rather than a third near-duplicate multipart endpoint just for issue photos.</summary>
    public long? PhotoDocumentId { get; set; }
    public int ReportedBy { get; set; }
    public DateTime ReportedAtUtc { get; set; }
    public bool IsResolved { get; set; }
    public int? ResolvedBy { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
    public string? ResolutionNotes { get; set; }
}

public static class TripIssueTypes
{
    public const string Breakdown = "Breakdown";
    public const string Accident = "Accident";
    public const string Delay = "Delay";
    public const string CustomerHold = "CustomerHold";
    public const string RouteBlocked = "RouteBlocked";
    public const string Other = "Other";
    public static readonly IReadOnlyList<string> All = [Breakdown, Accident, Delay, CustomerHold, RouteBlocked, Other];
}

/// <summary>Not enumerated by the FSD text itself — §23 only says "with severity", no fixed list. A reasonable,
/// documented three-level scale, the same "under-specified enum, pick something sensible and say so" handling
/// this register has used before (e.g. Trip Issues' own severity here, matching how CC-16 documents every place
/// it had to fill a gap the FSD left open, rather than inventing silently).</summary>
public static class IssueSeverities
{
    public const string Low = "Low";
    public const string Medium = "Medium";
    public const string High = "High";
    public static readonly IReadOnlyList<string> All = [Low, Medium, High];
}
