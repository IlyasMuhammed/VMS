namespace VMS.Modules.BusinessPartners.Domain;

/// <summary>Person or Company (FSD §6 field 2). Decides whether CNIC or NTN is the mandatory tax id.</summary>
public static class PartyTypes
{
    public const string Person = "Person";
    public const string Company = "Company";
    public static readonly IReadOnlyList<string> All = [Person, Company];
}

/// <summary>The roles a partner can hold (FSD §4, §7). A partner holds one or more at once.</summary>
public static class PartnerRoles
{
    public const string Driver = "Driver";
    public const string Workshop = "Workshop";
    public const string Bank = "Bank";
    public const string Vendor = "Vendor";
    public const string Customer = "Customer";
    public const string RunningCustomer = "RunningCustomer";
    public const string TrackerCompany = "TrackerCompany";
    public const string BodyMaker = "BodyMaker";
    public const string FuelCardCompany = "FuelCardCompany";

    public static readonly IReadOnlyList<string> All =
        [Driver, Workshop, Bank, Vendor, Customer, RunningCustomer, TrackerCompany, BodyMaker, FuelCardCompany];
}

/// <summary>§13.1. Active on save; changed only through the status action. Merged is set by the merge action (deferred, OQ-06).</summary>
public static class PartnerStatuses
{
    public const string Active = "Active";
    public const string Inactive = "Inactive";
    public const string Blacklisted = "Blacklisted";
    public const string Merged = "Merged";
    public static readonly IReadOnlyList<string> Settable = [Active, Inactive, Blacklisted];
}

public static class FilerStatuses
{
    public const string Filer = "Filer";
    public const string NonFiler = "NonFiler";
    public const string Unknown = "Unknown";
    public static readonly IReadOnlyList<string> All = [Filer, NonFiler, Unknown];
}

public static class AddressTypes
{
    public static readonly IReadOnlyList<string> All = ["Registered", "Billing", "Workshop", "Yard", "Correspondence"];
    public const string Registered = "Registered";
}

/// <summary>Field values for the role panels (FSD §7). Kept as plain strings so they read the same in the database, the API and the screen.</summary>
public static class RoleFieldValues
{
    public static readonly IReadOnlyList<string> LicenceTypes = ["LTV", "HTV", "Motorcycle", "Other"];
    public const string Employee = "Employee";
    public static readonly IReadOnlyList<string> EmploymentTypes = [Employee, "Contractor", "AdHoc"];
    public const string CommissionNone = "None";
    public const string CommissionPercent = "Percent";
    public const string CommissionFixed = "Fixed";
    public static readonly IReadOnlyList<string> CommissionBases = [CommissionNone, CommissionPercent, CommissionFixed];
    public static readonly IReadOnlyList<string> SupplyCategories = ["Parts", "Tyres", "Lubricants", "Fuel", "Toll", "Services", "Other"];
    public static readonly IReadOnlyList<string> CustomerTypes = ["Adda", "CargoCompany", "Factory", "Trader", "Other"];
    public static readonly IReadOnlyList<string> BillingCycles = ["PerTrip", "Weekly", "Fortnightly", "Monthly"];
    public static readonly IReadOnlyList<string> RateBases = ["PerTrip", "PerTonne", "PerKm", "MonthlyFixed"];
}
