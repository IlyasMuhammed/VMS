using VMS.Shared.Auditing;
using VMS.Shared.Common;

namespace VMS.Modules.Trips.Domain;

/// <summary>A customer's tax/deduction rule (FSD §14) — a deduction from the whole invoice amount, not per trip
/// line. Nothing is ever physically deleted or edited past its identity fields; a new rate for the same tax
/// automatically supersedes the old one (see <see cref="Services.CustomerTaxRuleService"/>).</summary>
internal sealed class CustomerTaxRule : ITenantScopedEntity, IAuditRooted
{
    public long CustomerTaxRuleId { get; set; }
    public Guid TenantId { get; set; }
    public int CustomerId { get; set; }

    public AuditRoot GetAuditRoot() => new("Customer", CustomerId.ToString());

    public string TaxName { get; set; } = string.Empty;
    /// <summary>Upper-cased <see cref="TaxName"/>, so "same tax name" matching (§14: "matched case-insensitively")
    /// is a plain column comparison/index rather than a query-time <c>UPPER()</c> on every row.</summary>
    public string TaxNameKey { get; set; } = string.Empty;
    public string TaxCode { get; set; } = string.Empty;
    public string TaxType { get; set; } = TaxTypes.Percentage;
    public decimal? TaxPercentage { get; set; }
    public decimal? FixedAmount { get; set; }
    public bool Applicable { get; set; } = true;
    public string CalculationBasis { get; set; } = CalculationBases.GrossTripAmount;
    public int Sequence { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public string Status { get; set; } = ActiveInactiveStatuses.Active;
    public long? SupersedesRuleId { get; set; }
    public string? Remarks { get; set; }
}

public static class TaxTypes
{
    public const string Percentage = "Percentage";
    public const string Fixed = "Fixed";
    public static readonly IReadOnlyList<string> All = [Percentage, Fixed];
}

public static class CalculationBases
{
    public const string GrossTripAmount = "GrossTripAmount";
    public const string InvoiceSubtotal = "InvoiceSubtotal";
    public static readonly IReadOnlyList<string> All = [GrossTripAmount, InvoiceSubtotal];
}
