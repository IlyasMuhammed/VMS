namespace VMS.Modules.Trips.Models;

/// <summary>§38's own wizard body: same customer as the old invoice (not carried in the request — it comes from
/// the invoice being regenerated), everything else editable and pre-filled with the old invoice's own values
/// when omitted.</summary>
public sealed class RegenerateInvoiceRequest
{
    public DateOnly? PeriodFrom { get; set; }
    public DateOnly? PeriodTo { get; set; }
    public DateOnly? InvoiceDate { get; set; }
    public long? CustomerInvoiceTemplateId { get; set; }
    public long? CustomerBillingAddressId { get; set; }
    public List<long> TripIds { get; set; } = [];
    public List<CreateInvoiceAdjustmentRequest> Adjustments { get; set; } = [];
    /// <summary>§38 step 3: "Re-price trips from rate master" (`Rate.Reprice`) — re-resolves rates for the
    /// selected trips and records the old/new snapshot in TripRateHistory before the new lines are built.</summary>
    public bool RepriceTrips { get; set; }
    public string RegenerationReason { get; set; } = string.Empty;
    public string? Remarks { get; set; }
}

public sealed class InvoiceRegenerationModel
{
    public InvoiceModel Invoice { get; set; } = null!;
    public long PreviousInvoiceId { get; set; }
    public string PreviousInvoiceNumber { get; set; } = string.Empty;
    /// <summary>§39's own warning line: "N trips on INV-… fall outside the new period and will become
    /// uninvoiced" — the old invoice's own trips that were not carried into the new selection.</summary>
    public List<string> ReleasedTripNumbers { get; set; } = [];
    /// <summary>§40/L13 (CC-36): every payment/advance/prior-transfer just moved from the old invoice to this
    /// one, 100%, uncapped — empty only when the old invoice was never Submitted (nothing was ever posted
    /// against it to move).</summary>
    public List<PaymentTransferModel> Transfers { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}
