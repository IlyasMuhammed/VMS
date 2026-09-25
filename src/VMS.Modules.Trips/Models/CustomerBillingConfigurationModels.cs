namespace VMS.Modules.Trips.Models;

public sealed class CustomerBillingConfigurationModel
{
    public long CustomerBillingConfigurationId { get; set; }
    public int CustomerId { get; set; }
    public int PaymentTermsDays { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public long? DefaultBillingAddressId { get; set; }
    public long? DefaultInvoiceTemplateId { get; set; }
    public string InvoiceNumberPrefix { get; set; } = string.Empty;
    public bool PodRequired { get; set; }
    public bool EvidenceRequired { get; set; }
    public int EvidencePageSize { get; set; }
    public string DuplicateReferenceBehaviour { get; set; } = string.Empty;
    public bool CustomerReferenceRequired { get; set; }
    public long? StatementEmailContactId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
}

public sealed class SaveCustomerBillingConfigurationRequest
{
    public int? PaymentTermsDays { get; set; }
    public string? CurrencyCode { get; set; }
    public long? DefaultBillingAddressId { get; set; }
    public long? DefaultInvoiceTemplateId { get; set; }
    public string? InvoiceNumberPrefix { get; set; }
    public bool PodRequired { get; set; }
    public bool EvidenceRequired { get; set; } = true;
    public int? EvidencePageSize { get; set; }
    public string? DuplicateReferenceBehaviour { get; set; }
    public bool CustomerReferenceRequired { get; set; }
    public long? StatementEmailContactId { get; set; }
    /// <summary>When to start this version; defaults to today. A new record supersedes the current one from this
    /// date on (§13: "effective-dated history kept").</summary>
    public DateOnly? EffectiveFrom { get; set; }
}
