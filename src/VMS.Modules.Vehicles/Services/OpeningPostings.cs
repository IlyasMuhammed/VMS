using VMS.Modules.Vehicles.Domain;

namespace VMS.Modules.Vehicles.Services;

/// <summary>A ledger entry about to be written (or, on the review step, about to be written on activation).</summary>
internal sealed record PostingPlan(string Type, string? SubType, decimal Amount, DateOnly Date, int? PartnerId, string Reference);

/// <summary>
/// The rows a vehicle's ledger opens with (FSD §19). They are worked out from what was entered, never typed a second time, and every
/// later total is a sum of them (BR-VH-003). The bank lease itself posts nothing: its schedule is an expectation, and installments post when paid.
/// </summary>
internal static class OpeningPostings
{
    /// <summary>
    /// The acquisition, what was paid, the registration cost and any security deposit, all dated by the acquisition date, not the day of
    /// activation (BR-VH-012). The down payment of a bank lease is the amount paid, so it is posted once, as the initial payment, and counts as
    /// part of the cost of the vehicle rather than a finance payment (OQ-08).
    /// </summary>
    public static IReadOnlyList<PostingPlan> ForVehicle(string registrationNo, DateOnly acquired, VehicleAcquisition? a, VehicleFinanceAgreement? agreement, string category, decimal? rentedDeposit, int? lessorId)
    {
        var plans = new List<PostingPlan>();
        if (a?.PurchasePrice is > 0)
            plans.Add(new PostingPlan(TransactionTypes.Acquisition, null, a.PurchasePrice.Value, acquired, a.SellerId, $"Vehicle acquisition — {registrationNo}"));

        if (a?.AmountPaid is > 0)
        {
            var how = string.Join(" ", new[] { a.PaymentMode, a.PaymentReference }.Where(s => !string.IsNullOrEmpty(s)));
            plans.Add(new PostingPlan(TransactionTypes.InitialPayment, agreement is null ? null : "DownPayment", a.AmountPaid.Value, acquired, a.SellerId ?? agreement?.BankId,
                how.Length == 0 ? "Initial payment" : $"Initial payment — {how}"));
        }

        if (a?.RegistrationCost is > 0)
            plans.Add(new PostingPlan(TransactionTypes.MajorExpense, "Registration", a.RegistrationCost.Value, acquired, a.SellerId, $"Registration — {registrationNo}"));

        if (category == OwnershipCategories.Rented && rentedDeposit is > 0)
            plans.Add(new PostingPlan(TransactionTypes.Deposit, null, rentedDeposit.Value, acquired, lessorId, "Security deposit"));
        else if (category == OwnershipCategories.BankLeased && agreement?.SecurityDeposit is > 0)
            plans.Add(new PostingPlan(TransactionTypes.Deposit, null, agreement.SecurityDeposit.Value, acquired, agreement.BankId, "Security deposit"));

        return plans;
    }

    /// <summary>The major expense an attached item's cost posts, dated by the installation date (never before the acquisition, which the item already respects).</summary>
    public static PostingPlan? ForItem(string itemType, string description, decimal? cost, DateOnly installed, int? supplierId) =>
        cost is > 0 ? new PostingPlan(TransactionTypes.MajorExpense, itemType, cost.Value, installed, supplierId, $"{itemType} — {description}") : null;
}
