using VMS.Modules.Trips.Domain;

namespace VMS.Modules.Trips.Services;

/// <summary>§36's own balance formula, recomputed from an invoice's own already-updated running totals — shared
/// by whichever action just changed one of those totals (a payment, CC-31; a reversal, CC-32; write-off/
/// discount, advances, transfers, carry-forward/refund, all still to come) so the formula itself never drifts
/// between callers. A pure function over the entity's own fields, not a DB read — the caller is responsible for
/// having already applied its own delta (e.g. <c>invoice.PaidAmount += amount</c>) before calling this.</summary>
internal static class InvoiceBalanceRecalculation
{
    public static void Apply(Invoice invoice)
    {
        invoice.BalanceAmount = invoice.NetAmount - invoice.PaidAmount - invoice.AdvanceAppliedAmount - invoice.WriteOffAmount - invoice.DiscountAmount
            - invoice.TransferredInAmount - invoice.CarryForwardInAmount + invoice.TransferredOutAmount + invoice.CarryForwardOutAmount + invoice.RefundedAmount;

        // §36 states this as "Unpaid (paid + transferred-in = 0), Partially Paid (0 < paid < Net), Paid (paid ≥
        // Net)" — literally true only when payments are the only thing that ever moved the balance. §37.5's own
        // words are the more general, and correct, rule: "the invoice becomes Paid when the balance reaches
        // zero" — a write-off/discount reaching zero balance with a SMALLER PaidAmount than Net must still read
        // Paid (proven by §40A.3's own worked example: 49,500 paid + 500 written off on a 50,000 Net invoice is
        // Paid, even though PaidAmount alone is less than Net). Restated in terms of how far the balance has
        // actually moved from Net, so it holds regardless of which combination of Dr/Cr reductions caused that.
        var appliedAgainstInvoice = invoice.NetAmount - invoice.BalanceAmount;
        invoice.PaymentStatus = invoice.BalanceAmount <= 0 ? InvoicePaymentStatuses.Paid
            : appliedAgainstInvoice > 0 ? InvoicePaymentStatuses.PartiallyPaid
            : InvoicePaymentStatuses.Unpaid;
    }
}
