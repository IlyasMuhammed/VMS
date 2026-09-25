using Microsoft.EntityFrameworkCore;
using VMS.Modules.Trips.Domain;
using VMS.Modules.Trips.Models;

namespace VMS.Modules.Trips.Services;

/// <summary>§42.5: INV-01..18.</summary>
internal sealed partial class LedgerReportService
{
    private IQueryable<Invoice> FilteredInvoices(ReportFilter f) => db.Invoices.AsNoTracking().Where(i => i.TenantId == tenant.TenantId
        && (f.From == null || i.InvoiceDate >= f.From) && (f.To == null || i.InvoiceDate <= f.To) && (f.CustomerId == null || i.CustomerId == f.CustomerId)
        && (string.IsNullOrEmpty(f.Status) || i.Status == f.Status) && (string.IsNullOrEmpty(f.PaymentStatus) || i.PaymentStatus == f.PaymentStatus)
        && (f.Active == null || i.IsActive == f.Active));

    private async Task<(List<string>, List<Dictionary<string, object?>>)> Inv01Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "invoiceNumber", "version", "invoiceDate", "periodFrom", "periodTo", "customerCode", "gross", "deductions", "net", "paid", "balance", "status" };
        var invoices = await FilteredInvoices(f).ToListAsync(ct);
        if (invoices.Count == 0) return (columns, []);
        var rows = invoices.Select(i => new Dictionary<string, object?>
        {
            ["invoiceNumber"] = i.InvoiceNumber, ["version"] = i.Version, ["invoiceDate"] = i.InvoiceDate, ["periodFrom"] = i.PeriodFrom, ["periodTo"] = i.PeriodTo,
            ["customerCode"] = i.CustomerCode, ["gross"] = i.GrossAmount, ["deductions"] = i.TotalDeduction, ["net"] = i.NetAmount, ["paid"] = i.PaidAmount,
            ["balance"] = i.BalanceAmount, ["status"] = i.Status
        }).ToList();
        return (columns, rows);
    }

    // INV-02: one summary row per invoice (the full nested detail is already served by GET /api/invoices/{id}).
    private async Task<(List<string>, List<Dictionary<string, object?>>)> Inv02Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "invoiceNumber", "version", "customerCode", "billToName", "lineCount", "adjustmentCount", "taxLineCount",
            "gross", "net", "balance", "paymentCount", "ledgerEntryCount" };
        if (f.InvoiceId is not { } invoiceId) return (columns, []);
        var invoice = await db.Invoices.AsNoTracking().FirstOrDefaultAsync(i => i.TenantId == tenant.TenantId && i.InvoiceId == invoiceId, ct);
        if (invoice is null) return (columns, []);

        var lineCount = await db.InvoiceLines.CountAsync(l => l.TenantId == tenant.TenantId && l.InvoiceId == invoiceId, ct);
        var adjustmentCount = await db.InvoiceAdjustments.CountAsync(a => a.TenantId == tenant.TenantId && a.InvoiceId == invoiceId, ct);
        var taxLineCount = await db.InvoiceTaxLines.CountAsync(t => t.TenantId == tenant.TenantId && t.InvoiceId == invoiceId, ct);
        var paymentCount = await db.InvoicePayments.CountAsync(p => p.TenantId == tenant.TenantId && p.InvoiceId == invoiceId, ct);
        var ledgerCount = await db.CustomerLedgerEntries.CountAsync(e => e.TenantId == tenant.TenantId && e.InvoiceId == invoiceId, ct);

        var row = new Dictionary<string, object?>
        {
            ["invoiceNumber"] = invoice.InvoiceNumber, ["version"] = invoice.Version, ["customerCode"] = invoice.CustomerCode,
            ["billToName"] = invoice.BillToAddressName ?? invoice.CustomerName, ["lineCount"] = lineCount, ["adjustmentCount"] = adjustmentCount,
            ["taxLineCount"] = taxLineCount, ["gross"] = invoice.GrossAmount, ["net"] = invoice.NetAmount, ["balance"] = invoice.BalanceAmount,
            ["paymentCount"] = paymentCount, ["ledgerEntryCount"] = ledgerCount
        };
        return (columns, [row]);
    }

    private async Task<(List<string>, List<Dictionary<string, object?>>)> Inv03Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "invoiceNumber", "lineNo", "tripNumber", "tripDate", "customerTripReference", "routeCode", "vehicleRegNo", "driverName", "rate", "amount" };
        var invoiceIds = await FilteredInvoices(f).Select(i => i.InvoiceId).ToListAsync(ct);
        if (invoiceIds.Count == 0) return (columns, []);
        var lines = await db.InvoiceLines.AsNoTracking().Where(l => l.TenantId == tenant.TenantId && invoiceIds.Contains(l.InvoiceId)
            && (f.VehicleId == null || l.VehicleId == f.VehicleId)).ToListAsync(ct);
        if (lines.Count == 0) return (columns, []);
        var invoiceNumbers = await db.Invoices.AsNoTracking().Where(i => i.TenantId == tenant.TenantId && invoiceIds.Contains(i.InvoiceId)).ToDictionaryAsync(i => i.InvoiceId, i => i.InvoiceNumber, ct);

        var rows = lines.Select(l => new Dictionary<string, object?>
        {
            ["invoiceNumber"] = invoiceNumbers.GetValueOrDefault(l.InvoiceId), ["lineNo"] = l.LineNo, ["tripNumber"] = l.TripNumber, ["tripDate"] = l.TripDate,
            ["customerTripReference"] = l.CustomerTripReference, ["routeCode"] = l.RouteCode, ["vehicleRegNo"] = l.VehicleRegNo, ["driverName"] = l.DriverName,
            ["rate"] = l.Rate, ["amount"] = l.Amount
        }).ToList();
        return (columns, rows);
    }

    // INV-04: "ready to bill" — a simpler, honestly-approximate cut than CC-24's own full eligibility search
    // (blocking reasons, overlap, etc.): every Completed, Active, still-uninvoiced trip in the period.
    private async Task<(List<string>, List<Dictionary<string, object?>>)> Inv04Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "tripNumber", "tripDate", "amount", "podStatus" };
        var trips = await db.Trips.AsNoTracking().Where(t => t.TenantId == tenant.TenantId && t.Status == TripStatuses.Completed && t.IsActive && t.InvoiceId == null
            && (f.CustomerId == null || t.CustomerId == f.CustomerId)
            && (f.From == null || t.CompletionDate >= f.From) && (f.To == null || t.CompletionDate <= f.To)).ToListAsync(ct);
        if (trips.Count == 0) return (columns, []);
        var tripIds = trips.Select(t => t.TripId).ToList();
        var latestPods = await db.TripPODs.AsNoTracking().Where(p => p.TenantId == tenant.TenantId && tripIds.Contains(p.TripId))
            .GroupBy(p => p.TripId).Select(g => g.OrderByDescending(p => p.UploadedAtUtc).First()).ToDictionaryAsync(p => p.TripId, ct);

        var rows = trips.Select(t => new Dictionary<string, object?>
        { ["tripNumber"] = t.TripNumber, ["tripDate"] = t.TripDate, ["amount"] = t.TripAmount, ["podStatus"] = latestPods.GetValueOrDefault(t.TripId)?.Status ?? "None" }).ToList();
        return (columns, rows);
    }

    private async Task<(List<string>, List<Dictionary<string, object?>>)> Inv05Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "tripNumber", "invoiceNumber", "lineAmount", "invoiceStatus" };
        var trips = await db.Trips.AsNoTracking().Where(t => t.TenantId == tenant.TenantId && t.InvoiceId != null
            && (f.From == null || t.TripDate >= f.From) && (f.To == null || t.TripDate <= f.To)).ToListAsync(ct);
        if (trips.Count == 0) return (columns, []);
        var invoiceIds = trips.Select(t => t.InvoiceId!.Value).Distinct().ToList();
        var invoices = await db.Invoices.AsNoTracking().Where(i => i.TenantId == tenant.TenantId && invoiceIds.Contains(i.InvoiceId)).ToDictionaryAsync(i => i.InvoiceId, ct);
        var lineAmounts = await db.InvoiceLines.AsNoTracking().Where(l => l.TenantId == tenant.TenantId && l.TripId != null && trips.Select(t => t.TripId).Contains(l.TripId!.Value))
            .ToDictionaryAsync(l => l.TripId!.Value, l => l.Amount, ct);

        var rows = trips.Select(t => new Dictionary<string, object?>
        {
            ["tripNumber"] = t.TripNumber, ["invoiceNumber"] = invoices.GetValueOrDefault(t.InvoiceId!.Value)?.InvoiceNumber,
            ["lineAmount"] = lineAmounts.GetValueOrDefault(t.TripId), ["invoiceStatus"] = invoices.GetValueOrDefault(t.InvoiceId!.Value)?.Status
        }).ToList();
        return (columns, rows);
    }

    private async Task<(List<string>, List<Dictionary<string, object?>>)> Inv06Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "tripNumber", "tripDate", "amount", "blockingReason", "ageDays" };
        var today = await clock.TodayAsync(tenant.TenantId);
        var trips = await db.Trips.AsNoTracking().Where(t => t.TenantId == tenant.TenantId && t.Status == TripStatuses.Completed && t.IsActive && t.InvoiceId == null
            && (f.CustomerId == null || t.CustomerId == f.CustomerId)
            && (f.From == null || t.CompletionDate >= f.From) && (f.To == null || t.CompletionDate <= f.To)).ToListAsync(ct);
        var rows = trips.Select(t => new Dictionary<string, object?>
        {
            ["tripNumber"] = t.TripNumber, ["tripDate"] = t.TripDate, ["amount"] = t.TripAmount,
            ["blockingReason"] = t.RateMissing ? "Rate missing" : "Not yet invoiced", ["ageDays"] = t.CompletionDate is { } cd ? today.DayNumber - cd.DayNumber : null
        }).ToList();
        return (columns, rows);
    }

    private async Task<(List<string>, List<Dictionary<string, object?>>)> Inv07Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "invoiceNumber", "net", "paid", "paidDate" };
        var invoices = await FilteredInvoices(f).Where(i => i.PaymentStatus == InvoicePaymentStatuses.Paid).ToListAsync(ct);
        if (invoices.Count == 0) return (columns, []);
        var invoiceIds = invoices.Select(i => i.InvoiceId).ToList();
        var paidDates = await db.CustomerLedgerEntries.AsNoTracking().Where(e => e.TenantId == tenant.TenantId && e.InvoiceId != null && invoiceIds.Contains(e.InvoiceId!.Value)
                && (e.EntryType == LedgerEntryTypes.Payment || e.EntryType == LedgerEntryTypes.WriteOff || e.EntryType == LedgerEntryTypes.Discount || e.EntryType == LedgerEntryTypes.TransferIn))
            .GroupBy(e => e.InvoiceId!.Value).Select(g => new { InvoiceId = g.Key, Last = g.Max(e => e.EntryDate) }).ToDictionaryAsync(g => g.InvoiceId, g => g.Last, ct);

        var rows = invoices.Select(i => new Dictionary<string, object?>
        { ["invoiceNumber"] = i.InvoiceNumber, ["net"] = i.NetAmount, ["paid"] = i.PaidAmount, ["paidDate"] = paidDates.GetValueOrDefault(i.InvoiceId) }).ToList();
        return (columns, rows);
    }

    private async Task<(List<string>, List<Dictionary<string, object?>>)> Inv08Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "invoiceNumber", "net", "paid", "balance" };
        var invoices = await FilteredInvoices(f).Where(i => i.PaymentStatus == InvoicePaymentStatuses.PartiallyPaid).ToListAsync(ct);
        var rows = invoices.Select(i => new Dictionary<string, object?> { ["invoiceNumber"] = i.InvoiceNumber, ["net"] = i.NetAmount, ["paid"] = i.PaidAmount, ["balance"] = i.BalanceAmount }).ToList();
        return (columns, rows);
    }

    private async Task<(List<string>, List<Dictionary<string, object?>>)> Inv09Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "invoiceNumber", "net", "dueDate", "daysOverdue" };
        var today = await clock.TodayAsync(tenant.TenantId);
        var invoices = await FilteredInvoices(f).Where(i => i.PaymentStatus == InvoicePaymentStatuses.Unpaid && i.Status == InvoiceStatuses.Submitted && i.IsActive).ToListAsync(ct);
        var rows = invoices.Select(i => new Dictionary<string, object?>
        { ["invoiceNumber"] = i.InvoiceNumber, ["net"] = i.NetAmount, ["dueDate"] = i.DueDate, ["daysOverdue"] = i.DueDate is { } d ? Math.Max(0, today.DayNumber - d.DayNumber) : (int?)null }).ToList();
        return (columns, rows);
    }

    private async Task<(List<string>, List<Dictionary<string, object?>>)> Inv10Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "invoiceNumber", "dueDate", "balance", "daysOverdue" };
        var asOf = f.AsOf ?? await clock.TodayAsync(tenant.TenantId);
        var invoices = await db.Invoices.AsNoTracking().Where(i => i.TenantId == tenant.TenantId && i.Status == InvoiceStatuses.Submitted && i.IsActive && i.BalanceAmount > 0
            && (f.CustomerId == null || i.CustomerId == f.CustomerId)).ToListAsync(ct);
        var rows = invoices.Select(i => new Dictionary<string, object?>
        {
            ["invoiceNumber"] = i.InvoiceNumber, ["dueDate"] = i.DueDate, ["balance"] = i.BalanceAmount,
            ["daysOverdue"] = i.DueDate is { } d ? Math.Max(0, asOf.DayNumber - d.DayNumber) : (int?)null
        }).ToList();
        return (columns, rows);
    }

    private async Task<(List<string>, List<Dictionary<string, object?>>)> Inv11Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "invoiceNumber", "adjustmentMonth", "amount", "note", "by" };
        var invoiceIds = await FilteredInvoices(f).Select(i => i.InvoiceId).ToListAsync(ct);
        if (invoiceIds.Count == 0) return (columns, []);
        var adjustments = await db.InvoiceAdjustments.AsNoTracking().Where(a => a.TenantId == tenant.TenantId && invoiceIds.Contains(a.InvoiceId)
            && (f.BalanceSign != "Cr" || a.AdjustmentAmount < 0) && (f.BalanceSign != "Dr" || a.AdjustmentAmount > 0)).ToListAsync(ct);
        if (adjustments.Count == 0) return (columns, []);
        var invoiceNumbers = await db.Invoices.AsNoTracking().Where(i => i.TenantId == tenant.TenantId && invoiceIds.Contains(i.InvoiceId)).ToDictionaryAsync(i => i.InvoiceId, i => i.InvoiceNumber, ct);

        var rows = adjustments.Select(a => new Dictionary<string, object?>
        { ["invoiceNumber"] = invoiceNumbers.GetValueOrDefault(a.InvoiceId), ["adjustmentMonth"] = a.AdjustmentMonth, ["amount"] = a.AdjustmentAmount, ["note"] = a.AdjustmentNote }).ToList();
        return (columns, rows);
    }

    private async Task<(List<string>, List<Dictionary<string, object?>>)> Inv12Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "invoiceNumber", "customerCode", "taxCode", "taxName", "applicable", "deduction" };
        var invoiceIds = await FilteredInvoices(f).Select(i => i.InvoiceId).ToListAsync(ct);
        if (invoiceIds.Count == 0) return (columns, []);
        var taxLines = await db.InvoiceTaxLines.AsNoTracking().Where(t => t.TenantId == tenant.TenantId && invoiceIds.Contains(t.InvoiceId)
            && (string.IsNullOrEmpty(f.Method) || t.TaxCode == f.Method)).ToListAsync(ct);
        if (taxLines.Count == 0) return (columns, []);
        var invoices = await db.Invoices.AsNoTracking().Where(i => i.TenantId == tenant.TenantId && invoiceIds.Contains(i.InvoiceId)).ToDictionaryAsync(i => i.InvoiceId, ct);

        var rows = taxLines.Select(t => new Dictionary<string, object?>
        {
            ["invoiceNumber"] = invoices.GetValueOrDefault(t.InvoiceId)?.InvoiceNumber, ["customerCode"] = invoices.GetValueOrDefault(t.InvoiceId)?.CustomerCode,
            ["taxCode"] = t.TaxCode, ["taxName"] = t.TaxName, ["applicable"] = t.Applicable, ["deduction"] = t.Amount
        }).ToList();
        return (columns, rows);
    }

    private async Task<(List<string>, List<Dictionary<string, object?>>)> Inv13Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "oldInvoiceNumber", "newInvoiceNumber", "reason", "by", "on", "netChange" };
        var regenerated = await db.Invoices.AsNoTracking().Where(i => i.TenantId == tenant.TenantId && i.PreviousInvoiceId != null
            && (f.From == null || i.RegeneratedOn >= f.From.Value.ToDateTime(TimeOnly.MinValue)) && (f.To == null || i.RegeneratedOn <= f.To.Value.ToDateTime(TimeOnly.MaxValue))
            && (f.CustomerId == null || i.CustomerId == f.CustomerId)).ToListAsync(ct);
        if (regenerated.Count == 0) return (columns, []);
        var previousIds = regenerated.Select(i => i.PreviousInvoiceId!.Value).Distinct().ToList();
        var previous = await db.Invoices.AsNoTracking().Where(i => i.TenantId == tenant.TenantId && previousIds.Contains(i.InvoiceId)).ToDictionaryAsync(i => i.InvoiceId, ct);

        var rows = regenerated.Select(i =>
        {
            var old = previous.GetValueOrDefault(i.PreviousInvoiceId!.Value);
            return new Dictionary<string, object?>
            {
                ["oldInvoiceNumber"] = old?.InvoiceNumber, ["newInvoiceNumber"] = i.InvoiceNumber, ["reason"] = i.RegenerationReason,
                ["by"] = i.RegeneratedBy, ["on"] = i.RegeneratedOn, ["netChange"] = old is null ? null : (decimal?)(i.NetAmount - old.NetAmount)
            };
        }).ToList();
        return (columns, rows);
    }

    private async Task<(List<string>, List<Dictionary<string, object?>>)> Inv14Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "invoiceNumber", "receiptNumber", "date", "amount", "method", "status" };
        var payments = await db.InvoicePayments.AsNoTracking().Where(p => p.TenantId == tenant.TenantId
            && (f.From == null || p.PaymentDate >= f.From) && (f.To == null || p.PaymentDate <= f.To)).ToListAsync(ct);
        if (payments.Count == 0) return (columns, []);
        var invoiceIds = payments.Select(p => p.InvoiceId).Distinct().ToList();
        var invoices = await db.Invoices.AsNoTracking().Where(i => i.TenantId == tenant.TenantId && invoiceIds.Contains(i.InvoiceId)
            && (f.CustomerId == null || i.CustomerId == f.CustomerId)).ToDictionaryAsync(i => i.InvoiceId, i => i.InvoiceNumber, ct);
        var receiptIds = payments.Select(p => p.CustomerReceiptId).Distinct().ToList();
        var receipts = await db.CustomerReceipts.AsNoTracking().Where(r => r.TenantId == tenant.TenantId && receiptIds.Contains(r.CustomerReceiptId)).ToDictionaryAsync(r => r.CustomerReceiptId, r => r.ReceiptNumber, ct);

        var rows = payments.Where(p => invoices.ContainsKey(p.InvoiceId)).Select(p => new Dictionary<string, object?>
        {
            ["invoiceNumber"] = invoices.GetValueOrDefault(p.InvoiceId), ["receiptNumber"] = receipts.GetValueOrDefault(p.CustomerReceiptId), ["date"] = p.PaymentDate,
            ["amount"] = p.Amount, ["method"] = p.PaymentMethod, ["status"] = p.Status
        }).ToList();
        return (columns, rows);
    }

    private async Task<(List<string>, List<Dictionary<string, object?>>)> Inv15Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "oldInvoiceNumber", "newInvoiceNumber", "sourceKind", "amount", "by", "reason" };
        var transfers = await db.InvoicePaymentTransfers.AsNoTracking().Where(t => t.TenantId == tenant.TenantId
            && (f.From == null || t.TransferDate >= f.From) && (f.To == null || t.TransferDate <= f.To)).ToListAsync(ct);
        if (transfers.Count == 0) return (columns, []);
        var invoiceIds = transfers.SelectMany(t => new[] { t.OldInvoiceId, t.NewInvoiceId }).Distinct().ToList();
        var invoices = await db.Invoices.AsNoTracking().Where(i => i.TenantId == tenant.TenantId && invoiceIds.Contains(i.InvoiceId)).ToDictionaryAsync(i => i.InvoiceId, i => i.InvoiceNumber, ct);

        var rows = transfers.Select(t => new Dictionary<string, object?>
        {
            ["oldInvoiceNumber"] = invoices.GetValueOrDefault(t.OldInvoiceId), ["newInvoiceNumber"] = invoices.GetValueOrDefault(t.NewInvoiceId),
            ["sourceKind"] = t.SourceKind, ["amount"] = t.AmountTransferred, ["by"] = t.TransferredBy, ["reason"] = t.Reason
        }).ToList();
        return (columns, rows);
    }

    private async Task<(List<string>, List<Dictionary<string, object?>>)> Inv16Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "invoiceNumber", "evidenceVersion", "generatedOn", "generatedBy", "pageCount", "lineCount", "status" };
        var evidences = await db.InvoiceEvidences.AsNoTracking().Where(e => e.TenantId == tenant.TenantId
            && (string.IsNullOrEmpty(f.Status) || e.Status == f.Status)
            && (f.From == null || e.GeneratedAtUtc >= f.From.Value.ToDateTime(TimeOnly.MinValue)) && (f.To == null || e.GeneratedAtUtc <= f.To.Value.ToDateTime(TimeOnly.MaxValue))).ToListAsync(ct);
        if (evidences.Count == 0) return (columns, []);
        var invoiceIds = evidences.Select(e => e.InvoiceId).Distinct().ToList();
        var invoices = await db.Invoices.AsNoTracking().Where(i => i.TenantId == tenant.TenantId && invoiceIds.Contains(i.InvoiceId)).ToDictionaryAsync(i => i.InvoiceId, i => i.InvoiceNumber, ct);

        var rows = evidences.Select(e => new Dictionary<string, object?>
        {
            ["invoiceNumber"] = invoices.GetValueOrDefault(e.InvoiceId), ["evidenceVersion"] = e.EvidenceVersion, ["generatedOn"] = e.GeneratedAtUtc, ["generatedBy"] = e.GeneratedBy,
            ["pageCount"] = e.PageCount, ["lineCount"] = e.LineCount, ["status"] = e.Status
        }).ToList();
        return (columns, rows);
    }

    private async Task<(List<string>, List<Dictionary<string, object?>>)> Inv17Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "invoiceNumber", "status", "reason", "replacedBy" };
        var invoices = await db.Invoices.AsNoTracking().Where(i => i.TenantId == tenant.TenantId && (i.Status == InvoiceStatuses.Cancelled || i.Status == InvoiceStatuses.Inactive)
            && (f.From == null || i.InvoiceDate >= f.From) && (f.To == null || i.InvoiceDate <= f.To)).ToListAsync(ct);
        if (invoices.Count == 0) return (columns, []);
        var replacedByIds = invoices.Where(i => i.ReplacedByInvoiceId != null).Select(i => i.ReplacedByInvoiceId!.Value).Distinct().ToList();
        var replacedByNumbers = replacedByIds.Count == 0 ? new Dictionary<long, string>() : await db.Invoices.AsNoTracking()
            .Where(i => i.TenantId == tenant.TenantId && replacedByIds.Contains(i.InvoiceId)).ToDictionaryAsync(i => i.InvoiceId, i => i.InvoiceNumber, ct);

        var rows = invoices.Select(i => new Dictionary<string, object?>
        {
            ["invoiceNumber"] = i.InvoiceNumber, ["status"] = i.Status, ["reason"] = i.Status == InvoiceStatuses.Cancelled ? i.CancelReason : i.RegenerationReason,
            ["replacedBy"] = i.ReplacedByInvoiceId is { } id ? replacedByNumbers.GetValueOrDefault(id) : null
        }).ToList();
        return (columns, rows);
    }

    private async Task<(List<string>, List<Dictionary<string, object?>>)> Inv18Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "customerCode", "dateRangeFrom", "dateRangeTo", "uninvoicedTrips", "amount" };
        var trips = await db.Trips.AsNoTracking().Where(t => t.TenantId == tenant.TenantId && t.Status == TripStatuses.Completed && t.IsActive && t.InvoiceId == null
            && (f.CustomerId == null || t.CustomerId == f.CustomerId)
            && (f.From == null || t.CompletionDate >= f.From) && (f.To == null || t.CompletionDate <= f.To)).ToListAsync(ct);
        if (trips.Count == 0) return (columns, []);
        var codes = await CustomerCodesAsync(trips.Select(t => t.CustomerId), ct);

        var rows = trips.GroupBy(t => t.CustomerId).Select(g => new Dictionary<string, object?>
        {
            ["customerCode"] = codes.GetValueOrDefault(g.Key), ["dateRangeFrom"] = g.Min(t => t.CompletionDate), ["dateRangeTo"] = g.Max(t => t.CompletionDate),
            ["uninvoicedTrips"] = g.Count(), ["amount"] = g.Sum(t => t.TripAmount ?? 0)
        }).ToList();
        return (columns, rows);
    }
}
