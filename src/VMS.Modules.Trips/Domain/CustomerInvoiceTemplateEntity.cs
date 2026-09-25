using VMS.Shared.Auditing;
using VMS.Shared.Common;

namespace VMS.Modules.Trips.Domain;

/// <summary>A customer's invoice format (FSD §15). Several template *names* can exist for one customer at once
/// (the dropdown when more than one is applicable); within one name, versions are effective-dated the same way
/// as <see cref="CustomerTaxRule"/> and <see cref="CustomerBillingConfiguration"/> — at most one open
/// (<see cref="EffectiveTo"/> null) row per (customer, template name).</summary>
internal sealed class CustomerInvoiceTemplate : ITenantScopedEntity, IAuditRooted
{
    public long CustomerInvoiceTemplateId { get; set; }
    public Guid TenantId { get; set; }
    public int CustomerId { get; set; }

    public AuditRoot GetAuditRoot() => new("Customer", CustomerId.ToString());

    public string TemplateName { get; set; } = string.Empty;
    /// <summary>Starts at 1; a new row for the same <see cref="TemplateName"/> increments it (§15: "new version on
    /// every file/layout change"). An old version's row is never edited or removed — reprinting an old invoice
    /// uses exactly the version it was generated with (§15's own edge case).</summary>
    public int Version { get; set; } = 1;
    public string TemplateType { get; set; } = InvoiceTemplateTypes.SystemStandard;
    /// <summary>The storage key of the template file, or a report id — meaningless for <c>SystemStandard</c> (the
    /// one layout CC-27 actually renders in Phase 1), required for every other type once one is chosen for a
    /// customer (§15: "not fixed now... Client Confirmation Required," pending Q2's samples).</summary>
    public string TemplateReference { get; set; } = string.Empty;
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsDefault { get; set; }
    public string Status { get; set; } = InvoiceTemplateStatuses.Draft;
}

public static class InvoiceTemplateTypes
{
    public const string SystemStandard = "SystemStandard";
    public const string HtmlPdf = "HTMLPDF";
    public const string DocumentTemplate = "DocumentTemplate";
    public const string ReportDefinition = "ReportDefinition";
    public static readonly IReadOnlyList<string> All = [SystemStandard, HtmlPdf, DocumentTemplate, ReportDefinition];
}

public static class InvoiceTemplateStatuses
{
    public const string Draft = "Draft";
    public const string Active = "Active";
    public const string Inactive = "Inactive";
    public static readonly IReadOnlyList<string> All = [Draft, Active, Inactive];
}
