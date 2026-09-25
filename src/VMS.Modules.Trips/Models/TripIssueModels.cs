namespace VMS.Modules.Trips.Models;

public sealed class TripIssueModel
{
    public long TripIssueId { get; set; }
    public long TripId { get; set; }
    public string IssueType { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public long? PhotoDocumentId { get; set; }
    public int ReportedBy { get; set; }
    public DateTime ReportedAtUtc { get; set; }
    public bool IsResolved { get; set; }
    public int? ResolvedBy { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
    public string? ResolutionNotes { get; set; }
}

public sealed class CreateTripIssueRequest
{
    public string IssueType { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public long? PhotoDocumentId { get; set; }
    /// <summary>§23: "An issue can put the trip On Hold" — the reporter's own choice, not automatic for any
    /// particular <see cref="IssueType"/> (the FSD names no rule for which types always do). When true, this
    /// reuses <c>ITripLifecycleService.HoldAsync</c> directly rather than a second, parallel hold path.</summary>
    public bool PutOnHold { get; set; }
}

public sealed class ResolveTripIssueRequest
{
    public string? ResolutionNotes { get; set; }
}
