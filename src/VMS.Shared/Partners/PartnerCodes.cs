namespace VMS.Shared.Partners;

/// <summary>
/// The role codes other modules ask for. They are the partner module's own codes, repeated here so a vehicle can say "a Bank"
/// without referencing the partner module; a test in the partner module keeps the two lists equal.
/// </summary>
public static class PartnerRoleCodes
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
}
