namespace VMS.Modules.Documents.Domain;

/// <summary>One row of a tenant's starting document type master (FSD §23A.2). Values every tenant starts with; a tenant may add its own and edit these under Administration.</summary>
public sealed record DocumentTypeSeed(
    string Code, string Name, AppliesTo AppliesTo, string? PartnerRole, bool IsExpirable, int? DefaultValidityValue, string? DefaultValidityUnit,
    bool IsPeriodic, string MandatoryLevel, bool RequiresDocumentNumber, bool HasCost);

/// <summary>The twelve document types of FSD §23A.2, checked against the FSD text itself by a test (the same discipline as S0-FND-13's lookup seeds).</summary>
public static class PlatformDocumentTypes
{
    public const string RegistrationBook = "REGISTRATION_BOOK";
    public const string InsurancePolicy = "INSURANCE_POLICY";
    public const string FitnessCertificate = "FITNESS_CERTIFICATE";
    public const string RoutePermit = "ROUTE_PERMIT";
    public const string TokenTaxReceipt = "TOKEN_TAX_RECEIPT";
    public const string LeaseFinanceAgreement = "LEASE_FINANCE_AGREEMENT";
    public const string PurchaseInvoice = "PURCHASE_INVOICE";
    public const string DrivingLicence = "DRIVING_LICENCE";
    public const string Cnic = "CNIC";
    public const string NtnCertificate = "NTN_CERTIFICATE";
    public const string ServiceRateAgreement = "SERVICE_RATE_AGREEMENT";
    public const string ChequeCopy = "CHEQUE_COPY";

    public static readonly IReadOnlyList<DocumentTypeSeed> Defaults =
    [
        new(RegistrationBook,       "Registration Book",         AppliesTo.Vehicle,         null,     false, null, null,     false, MandatoryLevels.Required, false, false),
        new(InsurancePolicy,        "Insurance Policy",          AppliesTo.Vehicle,         null,     true,  12,   ValidityUnits.Months, true,  MandatoryLevels.Warn,     true,  true),
        new(FitnessCertificate,     "Fitness Certificate",       AppliesTo.Vehicle,         null,     true,  12,   ValidityUnits.Months, true,  MandatoryLevels.Warn,     true,  true),
        new(RoutePermit,            "Route Permit",               AppliesTo.Vehicle,         null,     true,  12,   ValidityUnits.Months, true,  MandatoryLevels.Warn,     true,  true),
        new(TokenTaxReceipt,        "Token Tax Receipt",         AppliesTo.Vehicle,         null,     true,  12,   ValidityUnits.Months, true,  MandatoryLevels.None,     true,  true),
        new(LeaseFinanceAgreement,  "Lease / Finance Agreement", AppliesTo.Vehicle,         null,     false, null, null,     false, MandatoryLevels.Required, false, false),
        new(PurchaseInvoice,        "Purchase Invoice",          AppliesTo.Vehicle,         null,     false, null, null,     false, MandatoryLevels.None,     false, false),
        new(DrivingLicence,         "Driving Licence",           AppliesTo.BusinessPartner, "Driver", true,  5,    ValidityUnits.Years,  true,  MandatoryLevels.Required, true,  false),
        new(Cnic,                   "CNIC",                      AppliesTo.BusinessPartner, null,     true,  10,   ValidityUnits.Years,  true,  MandatoryLevels.Required, true,  false),
        new(NtnCertificate,         "NTN Certificate",           AppliesTo.BusinessPartner, null,     false, null, null,     false, MandatoryLevels.Warn,     false, false),
        new(ServiceRateAgreement,   "Service / Rate Agreement",  AppliesTo.BusinessPartner, null,     true,  null, null,     true,  MandatoryLevels.None,     false, false),
        new(ChequeCopy,             "Cheque Copy",                AppliesTo.BusinessPartner, null,     false, null, null,     false, MandatoryLevels.None,     false, false),
    ];
}
