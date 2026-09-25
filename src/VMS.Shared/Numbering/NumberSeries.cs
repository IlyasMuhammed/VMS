using Microsoft.EntityFrameworkCore;

namespace VMS.Shared.Numbering;

/// <summary>How often a series starts again at 1 (FSD §24.1).</summary>
public static class ResetPeriods
{
    public const string Yearly = "Yearly";
    public const string Monthly = "Monthly";
    public const string Never = "Never";

    public static readonly IReadOnlyList<string> All = [Yearly, Monthly, Never];
}

/// <summary>What a tenant's series looks like until it changes it under Administration.</summary>
public sealed record NumberSeriesDefault(string Code, string Entity, string Prefix, int Padding, string ResetPeriod);

/// <summary>
/// The series the platform allocates numbers from. A module that needs its own adds it here; a tenant's
/// row is created from the default the first time the series is used or listed.
/// </summary>
public static class NumberSeriesCodes
{
    public const string BusinessPartner = "BP";
    public const string Vehicle = "VH";
    public const string VehicleTransaction = "VT";
    public const string FinanceAgreement = "FA";
    public const string Document = "DOC";
    /// <summary>Trip/Billing/Invoicing/Customer Ledger FSD §10: "auto-suggest CUS-00001" — no year segment in the
    /// FSD's own example, so <see cref="ResetPeriods.Never"/> (matches CUS-00001, CUS-00002, ... forever).</summary>
    public const string Customer = "CUS";

    public static readonly IReadOnlyList<NumberSeriesDefault> Defaults =
    [
        new(BusinessPartner,    "Business Partner",    "BP",  5, ResetPeriods.Yearly),
        new(Vehicle,            "Vehicle",             "VH",  4, ResetPeriods.Yearly),
        new(VehicleTransaction, "Vehicle Transaction", "VT",  6, ResetPeriods.Yearly),
        new(FinanceAgreement,   "Finance Agreement",   "FA",  4, ResetPeriods.Yearly),
        new(Document,           "Document",            "DOC", 6, ResetPeriods.Yearly),
        new(Customer,           "Customer",            "CUS", 5, ResetPeriods.Never),
    ];
}

/// <summary>
/// Hands out the next number of a series. Numbers are gap-free (FSD §24.1): a number is only ever
/// consumed by a save that commits, because allocation happens inside that save's transaction and is
/// undone with it. A second save that needs the same series waits for the first to finish.
/// </summary>
public interface INumberSeries
{
    /// <summary>
    /// Allocates the next number, for example <c>BP-26-00147</c>. <paramref name="db"/> must already be in
    /// the transaction that saves the record the number is for (<see cref="TransactionExtensions.InTransactionAsync{T}"/>);
    /// without one the call throws rather than spend a number that a failed save would leave behind as a gap.
    /// </summary>
    /// <param name="date">The date whose year or month picks the period. Defaults to today in the tenant's operating time zone.</param>
    /// <param name="tenantId">Defaults to the signed-in user's tenant.</param>
    Task<string> NextAsync(DbContext db, string seriesCode, DateOnly? date = null, Guid? tenantId = null, CancellationToken cancellationToken = default);
}

/// <summary>Builds the printed number. Pure, so the format is testable without a database.</summary>
public static class NumberFormat
{
    /// <summary>Identifies which counter a date belongs to: the year, the month, or all of time.</summary>
    public static string PeriodKey(string resetPeriod, DateOnly date) => resetPeriod switch
    {
        ResetPeriods.Yearly => date.ToString("yyyy"),
        ResetPeriods.Monthly => date.ToString("yyyyMM"),
        _ => string.Empty
    };

    /// <summary><c>{prefix}-{YY}-{number}</c> yearly, <c>{prefix}-{YYMM}-{number}</c> monthly, <c>{prefix}-{number}</c> otherwise.</summary>
    public static string Format(string prefix, int padding, string resetPeriod, DateOnly date, long number)
    {
        var digits = number.ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(padding, '0');
        return resetPeriod switch
        {
            ResetPeriods.Yearly => $"{prefix}-{date:yy}-{digits}",
            ResetPeriods.Monthly => $"{prefix}-{date:yyMM}-{digits}",
            _ => $"{prefix}-{digits}"
        };
    }
}
