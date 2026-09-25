using VMS.Shared.Auditing;
using VMS.Shared.Common;

namespace VMS.Modules.Trips.Domain;

/// <summary>Customer-specific billing behaviour (FSD §13) — "configuration, not code." Effective-dated history:
/// exactly one row per customer has <see cref="EffectiveTo"/> null (the current one, kept by a filtered unique
/// index), the same "one open row" idiom <c>VehicleRecurringCharge</c> already uses for its own amendments.</summary>
internal sealed class CustomerBillingConfiguration : ITenantScopedEntity, IAuditRooted
{
    public long CustomerBillingConfigurationId { get; set; }
    public Guid TenantId { get; set; }
    public int CustomerId { get; set; }

    public AuditRoot GetAuditRoot() => new("Customer", CustomerId.ToString());

    public int PaymentTermsDays { get; set; } = 30;
    public string CurrencyCode { get; set; } = "PKR";
    public long? DefaultBillingAddressId { get; set; }
    /// <summary>No FK yet: Invoice Templates (CC-07) do not exist in this module yet. A plain id, validated once
    /// they do — the same forward-looking, honestly-scoped choice CC-03/04 already made for other cross-task
    /// dependencies (see e.g. <c>ICustomerActivationRequirement</c>).</summary>
    public long? DefaultInvoiceTemplateId { get; set; }
    public string InvoiceNumberPrefix { get; set; } = "INV";
    public bool PodRequired { get; set; }
    public bool EvidenceRequired { get; set; } = true;
    public int EvidencePageSize { get; set; } = 50;
    public string DuplicateReferenceBehaviour { get; set; } = DuplicateReferenceBehaviours.Warn;
    public bool CustomerReferenceRequired { get; set; }
    public long? StatementEmailContactId { get; set; }

    public DateOnly EffectiveFrom { get; set; }
    /// <summary>Null = the current configuration. Set automatically when a later configuration replaces it.</summary>
    public DateOnly? EffectiveTo { get; set; }
}

public static class DuplicateReferenceBehaviours
{
    public const string Allow = "Allow";
    public const string Warn = "Warn";
    public const string Block = "Block";
    public static readonly IReadOnlyList<string> All = [Allow, Warn, Block];
}
