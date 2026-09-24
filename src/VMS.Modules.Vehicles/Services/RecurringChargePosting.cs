using VMS.Modules.Vehicles.Domain;

namespace VMS.Modules.Vehicles.Services;

/// <summary>
/// Posts the ledger entry a recurring charge entry's confirmation writes (BR-VH-031), shared by a user's confirm click and the
/// generation job's Auto-post (BR-VH-028: the two differ only in who confirmed it and whether <c>IsSystemGenerated</c> is set).
/// </summary>
internal static class RecurringChargePosting
{
    public static async Task PostAsync(VehicleContext ctx, VehicleRecurringChargeEntry entry, VehicleRecurringCharge charge, string chargeTypeCode,
        decimal amount, DateOnly paidOn, string? mode, string? reference, string? remarks, int? confirmedByUserId, bool isAutoPosted, CancellationToken ct)
    {
        var tx = new VehicleTransaction
        {
            VehicleId = entry.VehicleId, Type = TransactionTypes.RecurringCharge, SubType = chargeTypeCode, Amount = amount, TransactionDate = paidOn,
            PartnerId = charge.PayeeId, Reference = Describe(chargeTypeCode, reference), Source = TransactionSources.RecurringCharge, IsSystemGenerated = isAutoPosted,
            CreatedBy = confirmedByUserId ?? 0, CreatedOn = DateTime.UtcNow
        };
        ctx.Db.Transactions.Add(tx);
        await ctx.Db.SaveChangesAsync(ct);

        entry.Status = ChargeEntryStatuses.Paid;
        entry.PaidAmount = amount;
        entry.PaidOn = paidOn;
        entry.PaymentMode = mode;
        entry.Reference = reference;
        entry.Remarks = remarks;
        entry.TransactionId = tx.VehicleTransactionId;
        entry.ConfirmedBy = confirmedByUserId;   // null for an auto-post: nobody confirmed it (BR-VH-028)
        entry.ConfirmedOn = isAutoPosted ? null : DateTime.UtcNow;
        await ctx.Db.SaveChangesAsync(ct);
    }

    /// <summary>What the ledger shows: the charge type in words, and the reference if there is one. The column holds 120 characters.</summary>
    private static string Describe(string chargeTypeCode, string? reference)
    {
        var text = string.Join(" — ", new[] { Humanize(chargeTypeCode), reference }.Where(s => !string.IsNullOrWhiteSpace(s)));
        return text.Length <= 120 ? text : text[..120];
    }

    private static string Humanize(string code) =>
        string.Join(" ", code.Split('_', StringSplitOptions.RemoveEmptyEntries).Select(w => char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant()));
}
