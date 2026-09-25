using VMS.Shared.Common;

namespace VMS.Modules.Trips.Domain;

/// <summary>§42.9: "Large exports (&gt; 50,000 rows) run as background jobs and notify the user when the file is
/// ready." One row per queued export; a small report never creates one at all — <see cref="ILedgerReportService.ExportOrQueueAsync"/>
/// renders those synchronously exactly as before.</summary>
internal sealed class ReportExportJob : ITenantScopedEntity
{
    public long ReportExportJobId { get; set; }
    public Guid TenantId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string FiltersJson { get; set; } = string.Empty;
    public string Status { get; set; } = ReportExportJobStatuses.Queued;
    public string? StorageKey { get; set; }
    public string? Sha256 { get; set; }
    public long? SizeBytes { get; set; }
    public string? ContentType { get; set; }
    public string? OriginalFileName { get; set; }
    public int RequestedBy { get; set; }
    public string RequestedByName { get; set; } = string.Empty;
    public DateTime RequestedOn { get; set; }
    public DateTime? CompletedOn { get; set; }
    public string? ErrorMessage { get; set; }
}

public static class ReportExportJobStatuses
{
    public const string Queued = "Queued";
    public const string Processing = "Processing";
    public const string Ready = "Ready";
    public const string Failed = "Failed";
}
