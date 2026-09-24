namespace VMS.Modules.Documents.Domain;

/// <summary>Who a document belongs to (FSD §23A): one engine, two owners.</summary>
public static class DocumentOwnerTypes
{
    public const string BusinessPartner = "BusinessPartner";
    public const string Vehicle = "Vehicle";
    public static readonly IReadOnlyList<string> All = [BusinessPartner, Vehicle];
}

/// <summary>FSD §23A.1 field "Applies To": which owner kinds a document type is offered for. A partner type can be flags of both; a type applying to partners can be narrowed to one role.</summary>
[Flags]
public enum AppliesTo
{
    None = 0,
    BusinessPartner = 1,
    Vehicle = 2,
    Both = BusinessPartner | Vehicle,
}

/// <summary>FSD §23A.1 field "Default Validity": the unit the number is counted in.</summary>
public static class ValidityUnits
{
    public const string Days = "Days";
    public const string Months = "Months";
    public const string Years = "Years";
    public static readonly IReadOnlyList<string> All = [Days, Months, Years];

    public static DateOnly Add(DateOnly from, int value, string unit) => unit switch
    {
        Days => from.AddDays(value),
        Years => from.AddYears(value),
        _ => from.AddMonths(value),
    };
}

/// <summary>
/// FSD §23A.1 field "Mandatory For Activation", refined to the three states §23A.2's seed table actually uses (Yes / Warn / No)
/// rather than the single bit the field's own row describes. <see cref="Required"/> is what BR-VH-015 and BR-BP-005 (OQ-04:
/// warn, not block) act on; <see cref="Warn"/> only ever shows on the Missing Documents report and the vehicle list flag (BR-DOC-004).
/// </summary>
public static class MandatoryLevels
{
    public const string None = "None";
    public const string Warn = "Warn";
    public const string Required = "Required";
    public static readonly IReadOnlyList<string> All = [None, Warn, Required];
}

/// <summary>FSD §23A.3: the lifecycle of one version. "Scheduled" and "Active"/"Superseded" in the FSD's own diagram are what <see cref="Active"/>, <see cref="ExpiringSoon"/> and <see cref="Superseded"/> below spell out.</summary>
public static class DocumentStatuses
{
    public const string Active = "Active";
    public const string ExpiringSoon = "ExpiringSoon";
    public const string Expired = "Expired";
    public const string Superseded = "Superseded";
    public const string Rejected = "Rejected";
    public static readonly IReadOnlyList<string> All = [Active, ExpiringSoon, Expired, Superseded, Rejected];

    /// <summary>Statuses recalculated nightly against today (BR-DOC-003); a Superseded or Rejected version never changes again.</summary>
    public static readonly IReadOnlyList<string> Live = [Active, ExpiringSoon, Expired];
}

/// <summary>The Recurring Charge Type codes (§19A) a document type's "Has Cost" renewal can offer to link to (BR-DOC-006), where one exists.</summary>
public static class DocumentChargeLink
{
    public static readonly IReadOnlyDictionary<string, string> ChargeTypeCodeByDocumentTypeCode = new Dictionary<string, string>
    {
        ["INSURANCE_POLICY"] = "INSURANCE_PREMIUM",
        ["ROUTE_PERMIT"] = "ROUTE_PERMIT",
        ["TOKEN_TAX_RECEIPT"] = "TOKEN_TAX",
        ["FITNESS_CERTIFICATE"] = "FITNESS_RENEWAL",
    };
}
