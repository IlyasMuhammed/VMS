namespace VMS.Modules.Trips.Models;

public sealed class SubmitInvoiceRequest
{
    public string RowVersion { get; set; } = string.Empty;
    /// <summary>Hand / Email / Portal (§36); defaults to Hand when omitted.</summary>
    public string? SubmissionChannel { get; set; }
    /// <summary>Defaults to today (the ledger date, §36).</summary>
    public DateOnly? SubmittedOn { get; set; }
    public long? AcknowledgementDocumentId { get; set; }
}

public sealed class CancelInvoiceRequest
{
    public string RowVersion { get; set; } = string.Empty;
    /// <summary>§36: "Cancel requires reason."</summary>
    public string Reason { get; set; } = string.Empty;
}
