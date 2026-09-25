namespace VMS.Modules.Trips.Models;

public sealed class CreateInvoiceAdjustmentRequest
{
    public string AdjustmentMonth { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Note { get; set; } = string.Empty;
    public string? ReferenceInvoiceNo { get; set; }
}

public sealed class CreateInvoiceRequest
{
    public int CustomerId { get; set; }
    public DateOnly PeriodFrom { get; set; }
    public DateOnly PeriodTo { get; set; }
    /// <summary>Defaults to today.</summary>
    public DateOnly? InvoiceDate { get; set; }
    /// <summary>Required only when more than one template is applicable (§15's own "choose one" case).</summary>
    public long? CustomerInvoiceTemplateId { get; set; }
    /// <summary>Defaults to the customer's own default Active address when omitted.</summary>
    public long? CustomerBillingAddressId { get; set; }
    public List<long> TripIds { get; set; } = [];
    public List<CreateInvoiceAdjustmentRequest> Adjustments { get; set; } = [];
    /// <summary>True saves the invoice as Draft; false (default) Generates it directly (§36: no approval step).</summary>
    public bool SaveAsDraft { get; set; }
    public string? Remarks { get; set; }
}

public sealed class InvoiceLineModel
{
    public long InvoiceLineId { get; set; }
    public int LineNo { get; set; }
    public long? TripId { get; set; }
    public string LineType { get; set; } = string.Empty;
    public string? TripNumber { get; set; }
    public DateOnly? TripDate { get; set; }
    public string? RouteLabel { get; set; }
    public string? VehicleRegNo { get; set; }
    public string? DriverName { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal Rate { get; set; }
    public decimal Amount { get; set; }
}

public sealed class InvoiceAdjustmentModel
{
    public long InvoiceAdjustmentId { get; set; }
    public string AdjustmentMonth { get; set; } = string.Empty;
    public decimal AdjustmentAmount { get; set; }
    public string AdjustmentNote { get; set; } = string.Empty;
    public string? ReferenceInvoiceNo { get; set; }
    public int Sequence { get; set; }
}

public sealed class InvoiceTaxLineModel
{
    public long InvoiceTaxLineId { get; set; }
    public string TaxName { get; set; } = string.Empty;
    public string TaxCode { get; set; } = string.Empty;
    public bool Applicable { get; set; }
    public decimal Amount { get; set; }
}

public sealed class InvoiceModel
{
    public long InvoiceId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public int Version { get; set; }
    public int CustomerId { get; set; }
    public string CustomerCode { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string CurrencyCode { get; set; } = string.Empty;
    public DateOnly PeriodFrom { get; set; }
    public DateOnly PeriodTo { get; set; }
    public DateOnly InvoiceDate { get; set; }
    public DateOnly? DueDate { get; set; }
    public long? CustomerInvoiceTemplateId { get; set; }
    public int? TemplateVersion { get; set; }
    public long? CustomerBillingAddressId { get; set; }
    public decimal TotalTripAmount { get; set; }
    public decimal TotalAdjustment { get; set; }
    public decimal GrossAmount { get; set; }
    public decimal TotalDeduction { get; set; }
    public decimal NetAmount { get; set; }
    // §36's own header-card breakdown (Net, Paid, Balance) — CC-23 built these columns on the entity ahead of
    // any caller; exposed on the model only now (CC-36), the first task whose own acceptance genuinely needs a
    // caller to read TransferredInAmount back, rather than adding them speculatively ahead of a real need.
    public decimal PaidAmount { get; set; }
    public decimal AdvanceAppliedAmount { get; set; }
    public decimal WriteOffAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TransferredInAmount { get; set; }
    public decimal TransferredOutAmount { get; set; }
    public decimal CarryForwardInAmount { get; set; }
    public decimal CarryForwardOutAmount { get; set; }
    public decimal RefundedAmount { get; set; }
    public decimal BalanceAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateOnly? SubmittedOn { get; set; }
    public string? SubmissionChannel { get; set; }
    public DateTime? CancelledOn { get; set; }
    public string? CancelReason { get; set; }
    public string? Remarks { get; set; }
    public string RowVersion { get; set; } = string.Empty;
    public IReadOnlyList<InvoiceLineModel> Lines { get; set; } = [];
    public IReadOnlyList<InvoiceAdjustmentModel> Adjustments { get; set; } = [];
    public IReadOnlyList<InvoiceTaxLineModel> TaxLines { get; set; } = [];
    /// <summary>§47.3's own literal response field. "Queued" right after generation (CC-25); from CC-28 on,
    /// <c>GetAsync</c> overwrites it with the latest InvoiceEvidence version's own real status.</summary>
    public string EvidenceStatus { get; set; } = "Queued";
    public IReadOnlyList<string> Warnings { get; set; } = [];
}
