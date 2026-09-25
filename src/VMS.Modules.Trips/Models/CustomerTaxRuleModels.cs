namespace VMS.Modules.Trips.Models;

public sealed class CustomerTaxRuleModel
{
    public long CustomerTaxRuleId { get; set; }
    public int CustomerId { get; set; }
    public string TaxName { get; set; } = string.Empty;
    public string TaxCode { get; set; } = string.Empty;
    public string TaxType { get; set; } = string.Empty;
    public decimal? TaxPercentage { get; set; }
    public decimal? FixedAmount { get; set; }
    public bool Applicable { get; set; }
    public string CalculationBasis { get; set; } = string.Empty;
    public int Sequence { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public string Status { get; set; } = string.Empty;
    public long? SupersedesRuleId { get; set; }
    public string? Remarks { get; set; }
}

public sealed class SaveCustomerTaxRuleRequest
{
    public string TaxName { get; set; } = string.Empty;
    public string TaxCode { get; set; } = string.Empty;
    public string TaxType { get; set; } = string.Empty;
    public decimal? TaxPercentage { get; set; }
    public decimal? FixedAmount { get; set; }
    public bool Applicable { get; set; } = true;
    public string CalculationBasis { get; set; } = string.Empty;
    public int Sequence { get; set; }
    public DateOnly? EffectiveFrom { get; set; }
    public string? Remarks { get; set; }
    /// <summary>True once the user has answered the "will be set Inactive from ... Continue?" prompt (§14). A
    /// first attempt that would replace an existing rule is refused with <c>TAX_RULE_REPLACEMENT_CONFIRMATION</c>
    /// until this is set — the same "ask once, then proceed" shape as Business Partner's own duplicate warning.</summary>
    public bool ConfirmReplace { get; set; }
}

public sealed class UpdateCustomerTaxRuleRequest
{
    public decimal? TaxPercentage { get; set; }
    public decimal? FixedAmount { get; set; }
    public bool Applicable { get; set; } = true;
    public string? CalculationBasis { get; set; }
    public int Sequence { get; set; }
    public string? Remarks { get; set; }
}
