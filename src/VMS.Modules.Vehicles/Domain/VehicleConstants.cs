namespace VMS.Modules.Vehicles.Domain;

/// <summary>FSD §23.1. A vehicle is Draft until activated, and Sold or Transferred is the end of its life in the fleet.</summary>
public static class VehicleStatuses
{
    public const string Draft = "Draft";
    public const string Active = "Active";
    public const string Assigned = "Assigned";
    public const string UnderMaintenance = "UnderMaintenance";
    public const string TemporarilyUnavailable = "TemporarilyUnavailable";
    public const string Retired = "Retired";
    public const string Sold = "Sold";
    public const string Transferred = "Transferred";

    public static readonly IReadOnlyList<string> All = [Draft, Active, Assigned, UnderMaintenance, TemporarilyUnavailable, Retired, Sold, Transferred];

    /// <summary>Statuses of a vehicle that is part of the fleet now: it can be driven, maintained and charged.</summary>
    public static readonly IReadOnlyList<string> InFleet = [Active, Assigned, UnderMaintenance, TemporarilyUnavailable];

    /// <summary>The statuses a person can move an in-fleet vehicle between (Assigned is set by trips, not by hand).</summary>
    public static readonly IReadOnlyList<string> Settable = [Active, UnderMaintenance, TemporarilyUnavailable];

    public static bool IsInFleet(string status) => InFleet.Contains(status);
    public static bool IsDisposed(string status) => status is Sold or Transferred;
}

/// <summary>FSD §17: how the fleet comes to have the vehicle.</summary>
public static class OwnershipCategories
{
    public const string SelfOwned = "SelfOwned";
    public const string Shared = "Shared";
    public const string Rented = "Rented";
    public const string CustomerArrangement = "CustomerArrangement";
    public const string BankLeased = "BankLeased";

    public static readonly IReadOnlyList<string> All = [SelfOwned, Shared, Rented, CustomerArrangement, BankLeased];
}

public static class FuelTypes
{
    public static readonly IReadOnlyList<string> All = ["Diesel", "Petrol", "CNG", "LPG", "Hybrid", "Electric"];
}

public static class CapacityUnits
{
    public static readonly IReadOnlyList<string> All = ["Tonne", "Kg", "Litre", "CFT", "Passengers"];
}

public static class AcquisitionTypes
{
    public static readonly IReadOnlyList<string> All = ["Purchase", "Lease", "Rent", "SharedInduction", "CustomerInduction"];
}

/// <summary>Field values for the category-specific sets of FSD §17.</summary>
public static class ArrangementValues
{
    public static readonly IReadOnlyList<string> SharingBases = ["ProfitShare", "RevenueShare", "FixedMonthly"];
    public const string FixedMonthly = "FixedMonthly";
    public static readonly IReadOnlyList<string> ExpenseSharingRules = ["AllExpensesSameRatio", "OnlyMajorExpenses", "EachBearsOwn"];
    public static readonly IReadOnlyList<string> RentFrequencies = ["Monthly", "Weekly", "PerTrip"];
    public const string Monthly = "Monthly";
    public static readonly IReadOnlyList<string> ArrangementTypes = ["DedicatedMonthly", "PerTrip", "RevenueShare"];
    public const string DedicatedMonthly = "DedicatedMonthly";
    public const string RevenueShare = "RevenueShare";
}

public static class ItemConditions
{
    public static readonly IReadOnlyList<string> All = ["New", "Used", "Refurbished"];
}

public static class ItemStatuses
{
    public const string Attached = "Attached";
    public const string Detached = "Detached";
    /// <summary>Moved to another vehicle; the chain continues on the row that points back at this one (BR-VH-013).</summary>
    public const string Transferred = "Transferred";
}

public static class OdometerSources
{
    public const string VehicleCreation = "VehicleCreation";
    public const string Manual = "Manual";
}

/// <summary>What a row of the lifecycle history records (FSD §23.1).</summary>
public static class LifecycleEvents
{
    public const string Created = "Created";
    public const string StatusChange = "StatusChange";
    public const string CategoryChange = "CategoryChange";
    public const string Disposal = "Disposal";
}

/// <summary>The three ways a vehicle leaves the fleet (BR-VH-023), and the status each ends in.</summary>
public static class DisposalKinds
{
    public const string Retire = "Retire";
    public const string Sell = "Sell";
    public const string Transfer = "Transfer";
    public static readonly IReadOnlyList<string> All = [Retire, Sell, Transfer];

    public static string StatusOf(string kind) => kind switch
    {
        Retire => VehicleStatuses.Retired,
        Sell => VehicleStatuses.Sold,
        _ => VehicleStatuses.Transferred
    };
}

/// <summary>Codes of the Vehicle Type list that need a load capacity (FSD §16.2 field 13). They are the codes the platform seeds.</summary>
public static class CapacityVehicleTypes
{
    public static readonly IReadOnlyList<string> Codes = ["TRUCK", "TRAILER", "TANKER", "PRIME_MOVER"];
}

/// <summary>What a row of the vehicle ledger is (FSD §19). Paid to date is the sum of the initial payment and the installments.</summary>
public static class TransactionTypes
{
    public const string Acquisition = "Acquisition";
    public const string InitialPayment = "InitialPayment";
    public const string MajorExpense = "MajorExpense";
    public const string Deposit = "Deposit";
    public const string Installment = "Installment";
    /// <summary>A confirmed or auto-posted recurring charge entry (FSD §19A.4, BR-VH-031): insurance, tracker fees, rent and the rest. A Bank Installment charge posts as <see cref="Installment"/> instead (BR-VH-029): it is the same ledger entry either way, just reached from the payables workbench.</summary>
    public const string RecurringCharge = "RecurringCharge";
    /// <summary>A reversing or correcting entry: a posting is never edited or deleted (BR-VH-011).</summary>
    public const string Adjustment = "Adjustment";
    public static readonly IReadOnlyList<string> All = [Acquisition, InitialPayment, MajorExpense, Deposit, Installment, RecurringCharge, Adjustment];
}

public static class TransactionSources
{
    public const string VehicleCreation = "VehicleCreation";
    public const string Manual = "Manual";
    /// <summary>Confirmed or auto-posted from a recurring charge entry (BR-VH-028): <c>IsSystemGenerated</c> tells the two apart, this tells them both apart from a one-off manual entry.</summary>
    public const string RecurringCharge = "RecurringCharge";
}

public static class PaymentModes
{
    public static readonly IReadOnlyList<string> All = ["Cash", "BankTransfer", "Cheque", "PayOrder"];
}

/// <summary>How often installments fall due (FSD §18.2), and how many months that is.</summary>
public static class FinanceFrequencies
{
    public const string Monthly = "Monthly";
    public const string Quarterly = "Quarterly";
    public const string HalfYearly = "HalfYearly";
    public static readonly IReadOnlyList<string> All = [Monthly, Quarterly, HalfYearly];

    public static int Months(string frequency) => frequency switch { Monthly => 1, Quarterly => 3, _ => 6 };
}

public static class AgreementStatuses
{
    /// <summary>Entered on a Draft vehicle: nothing is scheduled or posted until the vehicle is activated.</summary>
    public const string Draft = "Draft";
    public const string Active = "Active";
    public const string Settled = "Settled";
    public const string Closed = "Closed";
}

public static class InstallmentStatuses
{
    public const string Pending = "Pending";
    public const string PartiallyPaid = "PartiallyPaid";
    public const string Paid = "Paid";
}

/// <summary>The Finance Type list value that means no agreement: the vehicle was paid for outright.</summary>
public static class FinanceTypeCodes
{
    public const string FullyPaid = "FULLY_PAID";
}

/// <summary>The Recurring Charge Type codes business rules recognise, from the platform seed (FSD §19A.1). Rename freely, never change the code.</summary>
public static class ChargeTypeCodes
{
    public const string BankInstallment = "BANK_INSTALLMENT";
    public const string TrackerFee = "TRACKER_FEE";
    public const string VehicleRentPayable = "VEHICLE_RENT_PAYABLE";
    public const string SharedPartnerPayout = "SHARED_PARTNER_PAYOUT";

    /// <summary>The role a charge's payee must hold, where the FSD names one (§19A.1: "Bank for installments, Tracker Company for tracker fees"). A charge type not listed accepts any active partner.</summary>
    public static readonly IReadOnlyDictionary<string, string> PayeeRole = new Dictionary<string, string>
    {
        [BankInstallment] = VMS.Shared.Partners.PartnerRoleCodes.Bank,
        [TrackerFee] = VMS.Shared.Partners.PartnerRoleCodes.TrackerCompany,
    };

    /// <summary>Charge types tied to the vehicle's ownership relation rather than the vehicle itself: closed with the relation on a category change (BR-VH-035).</summary>
    public static readonly IReadOnlyList<string> RelationTied = [VehicleRentPayable, SharedPartnerPayout];
}

/// <summary>FSD §19A.1 field "Amount Basis". Variable and Percentage of income pre-fill the generated entry with the last paid amount; only Fixed may be Auto-post (BR-VH-027).</summary>
public static class ChargeAmountBases
{
    public const string Fixed = "Fixed";
    public const string Variable = "Variable";
    public const string PercentageOfIncome = "PercentageOfIncome";
    public static readonly IReadOnlyList<string> All = [Fixed, Variable, PercentageOfIncome];
}

/// <summary>FSD §19A.1 field "Frequency". <see cref="Months"/> is the step for every frequency but Weekly and CustomDays, which step by days.</summary>
public static class ChargeFrequencies
{
    public const string Monthly = "Monthly";
    public const string Quarterly = "Quarterly";
    public const string HalfYearly = "HalfYearly";
    public const string Yearly = "Yearly";
    public const string Weekly = "Weekly";
    public const string CustomDays = "CustomDays";
    public static readonly IReadOnlyList<string> All = [Monthly, Quarterly, HalfYearly, Yearly, Weekly, CustomDays];

    public static int? Months(string frequency) => frequency switch { Monthly => 1, Quarterly => 3, HalfYearly => 6, Yearly => 12, _ => null };
}

/// <summary>FSD §19A.1 field "Posting Mode" and §19A.2. Auto-post is Fixed-amount only (BR-VH-027) and Admin-only (BR-VH-028).</summary>
public static class ChargePostingModes
{
    public const string GenerateAsDue = "GenerateAsDue";
    public const string AutoPost = "AutoPost";
    public const string ReminderOnly = "ReminderOnly";
    public static readonly IReadOnlyList<string> All = [GenerateAsDue, AutoPost, ReminderOnly];
}

/// <summary>FSD §19A.4: the lifecycle of one generated entry. "Scheduled" in the FSD's diagram is the charge's next occurrence before it is due — nothing materialises a row for it, so it is not a status any row holds.</summary>
public static class ChargeEntryStatuses
{
    public const string Due = "Due";
    public const string Overdue = "Overdue";
    public const string Paid = "Paid";
    public const string Waived = "Waived";
    public const string Cancelled = "Cancelled";
}