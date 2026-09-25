namespace VMS.Modules.Trips.Models;

public sealed class CustomerInvoiceTemplateModel
{
    public long CustomerInvoiceTemplateId { get; set; }
    public int CustomerId { get; set; }
    public string TemplateName { get; set; } = string.Empty;
    public int Version { get; set; }
    public string TemplateType { get; set; } = string.Empty;
    public string TemplateReference { get; set; } = string.Empty;
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsDefault { get; set; }
    public string Status { get; set; } = string.Empty;
}

public sealed class CreateCustomerInvoiceTemplateRequest
{
    public string TemplateName { get; set; } = string.Empty;
    public string? TemplateType { get; set; }
    public string? TemplateReference { get; set; }
}

public sealed class ActivateCustomerInvoiceTemplateRequest
{
    public DateOnly? EffectiveFrom { get; set; }
    public bool IsDefault { get; set; }
}

public sealed class NewInvoiceTemplateVersionRequest
{
    public string? TemplateType { get; set; }
    public string? TemplateReference { get; set; }
}

/// <summary>What an invoice-generation screen needs to know (FSD AC-07/AC-08): the actual choosing happens in a
/// later CC task (invoice generation does not exist yet) — this is the logic it will call.</summary>
public sealed class ApplicableInvoiceTemplatesModel
{
    public IReadOnlyList<CustomerInvoiceTemplateModel> Templates { get; set; } = [];
    /// <summary>True when more than one is applicable and the caller must choose (AC-08); false when exactly one
    /// (auto-selected, AC-07) or none (§15: "No active invoice format is configured for this customer").</summary>
    public bool RequiresSelection { get; set; }
}
