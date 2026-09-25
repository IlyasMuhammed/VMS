namespace VMS.Shared.Lookups;

/// <summary>
/// The master lists of FSD §24.2, with the values every tenant starts with. A tenant adds, renames,
/// reorders and retires values under Administration; these are only the starting point, and the
/// client's own lists replace them (the register asks for them at M1).
/// </summary>
public static class PlatformLookups
{
    public const string VehicleType = "VEHICLE_TYPE";
    public const string Make = "MAKE";
    public const string BodyType = "BODY_TYPE";
    public const string AxleConfiguration = "AXLE_CONFIGURATION";
    public const string AttachedItemType = "ATTACHED_ITEM_TYPE";
    public const string BpDocumentType = "BP_DOCUMENT_TYPE";
    public const string VehicleDocumentType = "VEHICLE_DOCUMENT_TYPE";
    public const string ExpenseType = "EXPENSE_TYPE";
    public const string IncomeType = "INCOME_TYPE";
    public const string FinanceType = "FINANCE_TYPE";
    public const string RecurringChargeType = "RECURRING_CHARGE_TYPE";
    public const string Province = "PROVINCE";
    public const string City = "CITY";
    public const string Country = "COUNTRY";
    public const string TripExpenseType = "TRIP_EXPENSE_TYPE";
    public const string TripIncomeType = "TRIP_INCOME_TYPE";

    /// <summary>Codes are the description in capitals with underscores, so a seed reads at a glance.</summary>
    private static string CodeOf(string description) =>
        new string(description.ToUpperInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray()).Trim('_');

    private static IReadOnlyList<LookupSeed> Seeds(params string[] descriptions) =>
        descriptions.Select(d => new LookupSeed(CodeOf(d), d)).ToList();

    private static LookupSeed Doc(string description, bool expirable) =>
        new(CodeOf(description), description, new Dictionary<string, string?> { ["expirable"] = expirable ? "true" : "false" });

    private static readonly LookupAttribute Expirable = new("expirable", "Expires", LookupAttributeKind.Flag);

    private static LookupSeed CitySeed(string description, string province) =>
        new(CodeOf(description), description, new Dictionary<string, string?> { ["province"] = province });

    /// <summary>The larger cities of each province, as a starting list. A tenant adds its own.</summary>
    private static readonly (string City, string Province)[] PakistaniCities =
    [
        ("Lahore", "Punjab"), ("Faisalabad", "Punjab"), ("Rawalpindi", "Punjab"), ("Multan", "Punjab"), ("Gujranwala", "Punjab"),
        ("Sialkot", "Punjab"), ("Bahawalpur", "Punjab"), ("Sargodha", "Punjab"), ("Sheikhupura", "Punjab"), ("Gujrat", "Punjab"),
        ("Jhang", "Punjab"), ("Sahiwal", "Punjab"), ("Okara", "Punjab"), ("Rahim Yar Khan", "Punjab"), ("Dera Ghazi Khan", "Punjab"),
        ("Kasur", "Punjab"), ("Mianwali", "Punjab"), ("Attock", "Punjab"), ("Chakwal", "Punjab"), ("Jhelum", "Punjab"),
        ("Wazirabad", "Punjab"), ("Hafizabad", "Punjab"), ("Khanewal", "Punjab"), ("Vehari", "Punjab"),

        ("Karachi", "Sindh"), ("Hyderabad", "Sindh"), ("Sukkur", "Sindh"), ("Larkana", "Sindh"), ("Nawabshah", "Sindh"),
        ("Mirpur Khas", "Sindh"), ("Jacobabad", "Sindh"), ("Shikarpur", "Sindh"), ("Khairpur", "Sindh"), ("Thatta", "Sindh"),

        ("Peshawar", "Khyber Pakhtunkhwa"), ("Mardan", "Khyber Pakhtunkhwa"), ("Abbottabad", "Khyber Pakhtunkhwa"), ("Mingora", "Khyber Pakhtunkhwa"),
        ("Kohat", "Khyber Pakhtunkhwa"), ("Bannu", "Khyber Pakhtunkhwa"), ("Dera Ismail Khan", "Khyber Pakhtunkhwa"), ("Mansehra", "Khyber Pakhtunkhwa"),
        ("Nowshera", "Khyber Pakhtunkhwa"), ("Charsadda", "Khyber Pakhtunkhwa"), ("Swabi", "Khyber Pakhtunkhwa"),

        ("Quetta", "Balochistan"), ("Gwadar", "Balochistan"), ("Turbat", "Balochistan"), ("Khuzdar", "Balochistan"),
        ("Sibi", "Balochistan"), ("Zhob", "Balochistan"), ("Chaman", "Balochistan"), ("Hub", "Balochistan"),

        ("Islamabad", "Islamabad Capital Territory"),

        ("Muzaffarabad", "Azad Jammu and Kashmir"), ("Mirpur", "Azad Jammu and Kashmir"), ("Kotli", "Azad Jammu and Kashmir"),

        ("Gilgit", "Gilgit-Baltistan"), ("Skardu", "Gilgit-Baltistan"),
    ];

    public static readonly IReadOnlyList<LookupTypeDefinition> All =
    [
        new(VehicleType, "Vehicle Type", "What kind of vehicle it is.",
            Defaults: Seeds("Truck", "Trailer", "Prime Mover", "Tanker", "Pickup", "Bus", "Car", "Bike", "Other")),

        new(Make, "Make", "The manufacturer.",
            Defaults: Seeds("Hino", "Isuzu", "Nissan", "Mercedes", "Master", "Toyota", "Suzuki", "FAW", "Shehzore", "Other")),

        new(BodyType, "Body Type", "The body fitted to the chassis.",
            Defaults: Seeds("Open", "Container", "Tanker", "Flatbed", "Refrigerated", "Dumper")),

        new(AxleConfiguration, "Axle Configuration", "Wheels and driven axles.",
            Defaults: Seeds("4x2", "6x2", "6x4", "10-wheeler", "22-wheeler")),

        new(AttachedItemType, "Attached Item Type", "Equipment fitted to a vehicle that has a value of its own.",
            Defaults: Seeds("Container", "AC Unit", "Tyre Set", "Crane", "Tanker Body", "Refrigeration Unit", "Tail Lift", "Tracker Device", "Other")),

        // Document types get their full settings (validity, lead days, mandatory, retention…) from the
        // document module (§23A.1); here they are the lists a form needs, with the one flag §24.2 names.
        new(BpDocumentType, "Business Partner Document Type", "Documents kept for a business partner.",
            [Expirable],
            [Doc("CNIC", true), Doc("Driving Licence", true), Doc("NTN Certificate", false), Doc("Agreement", true), Doc("Cheque Copy", false), Doc("Other", false)]),

        new(VehicleDocumentType, "Vehicle Document Type", "Documents kept for a vehicle.",
            [Expirable],
            [Doc("Registration Book", false), Doc("Insurance", true), Doc("Fitness", true), Doc("Route Permit", true), Doc("Token Tax", true), Doc("Lease Agreement", true), Doc("Purchase Invoice", false)]),

        new(ExpenseType, "Expense Type", "What a vehicle expense was for.",
            Defaults: Seeds("Engine", "Tyres", "Body Work", "Container", "AC", "Major Repair", "Registration", "Insurance", "Fuel", "Toll", "Parking", "Driver Allowance", "Other")),

        new(IncomeType, "Income Type", "Where a vehicle's income came from.",
            Defaults: Seeds("Monthly hire", "Trip income", "Rental income", "Other")),

        // Business rules recognise these by code (FULLY_PAID, BANK_LEASE, IJARAH, LOAN): rename them freely, never change the code.
        new(FinanceType, "Finance Type", "How a vehicle was paid for.",
            Defaults: Seeds("Fully Paid", "Bank Lease", "Ijarah", "Loan")),

        // Business rules recognise BANK_INSTALLMENT, TRACKER_FEE, VEHICLE_RENT_PAYABLE and SHARED_PARTNER_PAYOUT by code (§19A.1, §19A.3, BR-VH-035): rename freely, never change those codes.
        new(RecurringChargeType, "Recurring Charge Type", "What a vehicle's periodic obligation is for.",
            Defaults: Seeds("Bank Installment", "Insurance Premium", "Tracker Fee", "Token Tax", "Route Permit", "Fitness Renewal", "Fuel Card Fee",
                "Vehicle Rent Payable", "Maintenance Contract", "Shared Partner Payout", "Driver Salary", "Other")),

        new(Province, "Province", "Provinces and territories, for grouping cities.",
            Defaults: Seeds("Punjab", "Sindh", "Khyber Pakhtunkhwa", "Balochistan", "Islamabad Capital Territory", "Azad Jammu and Kashmir", "Gilgit-Baltistan")),

        new(City, "City", "Cities, each in a province.",
            [new LookupAttribute("province", "Province", LookupAttributeKind.Lookup, Required: true, LookupType: Province)],
            Cities),

        // Trip/Billing/Invoicing/Customer Ledger FSD §10: Customer.CountryId, "Default Pakistan". A tenant adds
        // more only if it actually bills a customer registered outside Pakistan.
        new(Country, "Country", "A customer's registered country.",
            Defaults: Seeds("Pakistan")),

        // Trip/Billing/Invoicing/Customer Ledger FSD §29: "extensible" — Fuel is seeded for completeness (the
        // FSD's own literal list) but never a valid choice on a new TripExpense (CC-20's own service refuses it —
        // "use the Fuel screen" is enforced there, not by leaving it out of the list a tenant could otherwise edit).
        new(TripExpenseType, "Trip Expense Type", "What a trip expense was for.",
            Defaults: Seeds("Fuel", "Toll Tax", "Traffic Challan", "Vehicle Service", "Tyre Air Check", "Parking", "Loading/Unloading", "Driver Expense", "Repair", "Other")),

        // Trip/Billing/Invoicing/Customer Ledger FSD §30: additional operational revenue attached to a trip —
        // its own list, distinct from the Vehicle module's existing IncomeType ("Monthly hire", "Rental income", …).
        new(TripIncomeType, "Trip Income Type", "Where a trip's additional income came from.",
            Defaults: Seeds("Detention", "Extra Drop", "Loading Recovery", "Other")),
    ];

    private static IReadOnlyList<LookupSeed> Cities => PakistaniCities.Select(x => CitySeed(x.City, CodeOf(x.Province))).ToList();
}
