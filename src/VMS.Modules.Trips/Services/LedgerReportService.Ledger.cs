using Microsoft.EntityFrameworkCore;
using VMS.Modules.Trips.Domain;
using VMS.Modules.Trips.Models;

namespace VMS.Modules.Trips.Services;

/// <summary>§42.2: LED-01..15 (CC-41).</summary>
internal sealed partial class LedgerReportService
{
    // LED-01: Customer Ledger Statement — customer required, running balance.
    private async Task<(List<string>, List<Dictionary<string, object?>>)> Led01Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "entryDate", "entryNumber", "documentNo", "entryType", "invoiceNumber", "narration", "debit", "credit", "runningBalance" };
        if (f.CustomerId is not { } customerId) return (columns, []);

        var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.TenantId == tenant.TenantId && c.CustomerId == customerId, ct)
            ?? throw new VMS.Shared.Exceptions.NotFoundException($"Customer {customerId} was not found.");
        var currency = customer.CurrencyCode;
        var all = await db.CustomerLedgerEntries.AsNoTracking().Where(e => e.TenantId == tenant.TenantId && e.CustomerId == customerId && e.CurrencyCode == currency)
            .OrderBy(e => e.EntryDate).ThenBy(e => e.CustomerSeq).ToListAsync(ct);

        var opening = f.From is null ? 0 : all.Where(e => e.EntryDate < f.From).Sum(e => e.DebitAmount - e.CreditAmount);
        var period = all.Where(e => (f.From is null || e.EntryDate >= f.From) && (f.To is null || e.EntryDate <= f.To)).ToList();
        var invoiceIds = period.Where(e => e.InvoiceId is not null).Select(e => e.InvoiceId!.Value).Distinct().ToList();
        var invoiceNumbers = invoiceIds.Count == 0 ? new Dictionary<long, string>() : await db.Invoices.AsNoTracking()
            .Where(i => i.TenantId == tenant.TenantId && invoiceIds.Contains(i.InvoiceId)).ToDictionaryAsync(i => i.InvoiceId, i => i.InvoiceNumber, ct);

        var running = opening;
        var rows = new List<Dictionary<string, object?>>();
        foreach (var e in period)
        {
            running += e.DebitAmount - e.CreditAmount;
            rows.Add(new Dictionary<string, object?>
            {
                ["entryDate"] = e.EntryDate, ["entryNumber"] = e.EntryNumber, ["documentNo"] = e.DocumentNo, ["entryType"] = e.EntryType,
                ["invoiceNumber"] = e.InvoiceId is { } id ? invoiceNumbers.GetValueOrDefault(id) : null, ["narration"] = e.Narration,
                ["debit"] = e.DebitAmount, ["credit"] = e.CreditAmount, ["runningBalance"] = running
            });
        }
        return (columns, rows);
    }

    // LED-02: Invoice-wise Ledger — customer required, one row per invoice with its own ledger balance.
    private async Task<(List<string>, List<Dictionary<string, object?>>)> Led02Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "invoiceNumber", "invoiceDate", "invoiceBalance", "ledgerBalance", "reconciled" };
        if (f.CustomerId is not { } customerId) return (columns, []);

        var invoices = await db.Invoices.AsNoTracking().Where(i => i.TenantId == tenant.TenantId && i.CustomerId == customerId
            && (f.InvoiceId == null || i.InvoiceId == f.InvoiceId) && (f.From == null || i.InvoiceDate >= f.From) && (f.To == null || i.InvoiceDate <= f.To))
            .ToListAsync(ct);
        var invoiceIds = invoices.Select(i => i.InvoiceId).ToList();
        var ledgerBalances = await db.CustomerLedgerEntries.AsNoTracking().Where(e => e.TenantId == tenant.TenantId && e.InvoiceId != null && invoiceIds.Contains(e.InvoiceId!.Value))
            .GroupBy(e => e.InvoiceId!.Value).Select(g => new { InvoiceId = g.Key, Balance = g.Sum(e => e.DebitAmount - e.CreditAmount) }).ToDictionaryAsync(g => g.InvoiceId, g => g.Balance, ct);

        var rows = invoices.Select(i =>
        {
            var ledgerBalance = ledgerBalances.GetValueOrDefault(i.InvoiceId, 0m);
            return new Dictionary<string, object?>
            {
                ["invoiceNumber"] = i.InvoiceNumber, ["invoiceDate"] = i.InvoiceDate, ["invoiceBalance"] = i.BalanceAmount,
                ["ledgerBalance"] = ledgerBalance, ["reconciled"] = ledgerBalance == i.BalanceAmount
            };
        }).ToList();
        return (columns, rows);
    }

    // LED-03: Customer Balances Summary.
    private async Task<(List<string>, List<Dictionary<string, object?>>)> Led03Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "customerCode", "customerName", "currencyCode", "totalDebits", "totalCredits", "balance", "overdue", "lastPayment" };
        var today = await clock.TodayAsync(tenant.TenantId);

        var balances = await db.CustomerBalances.AsNoTracking().Where(b => b.TenantId == tenant.TenantId
            && (f.CustomerId == null || b.CustomerId == f.CustomerId)).ToListAsync(ct);
        if (balances.Count == 0) return (columns, []);

        var customerIds = balances.Select(b => b.CustomerId).Distinct().ToList();
        var customers = await db.Customers.AsNoTracking().Where(c => c.TenantId == tenant.TenantId && customerIds.Contains(c.CustomerId)).ToDictionaryAsync(c => c.CustomerId, ct);

        var rows = new List<Dictionary<string, object?>>();
        foreach (var balance in balances)
        {
            if (f.BalanceSign == "Dr" && balance.BalanceAmount < 0) continue;
            if (f.BalanceSign == "Cr" && balance.BalanceAmount >= 0) continue;

            var customer = customers.GetValueOrDefault(balance.CustomerId);
            var totals = await db.CustomerLedgerEntries.AsNoTracking()
                .Where(e => e.TenantId == tenant.TenantId && e.CustomerId == balance.CustomerId && e.CurrencyCode == balance.CurrencyCode
                    && (f.AsOf == null || e.EntryDate <= f.AsOf))
                .GroupBy(e => 1).Select(g => new { Dr = g.Sum(e => e.DebitAmount), Cr = g.Sum(e => e.CreditAmount) }).FirstOrDefaultAsync(ct);
            var lastPayment = await db.CustomerLedgerEntries.AsNoTracking()
                .Where(e => e.TenantId == tenant.TenantId && e.CustomerId == balance.CustomerId && e.CurrencyCode == balance.CurrencyCode && e.EntryType == LedgerEntryTypes.Payment)
                .OrderByDescending(e => e.EntryDate).Select(e => (DateOnly?)e.EntryDate).FirstOrDefaultAsync(ct);
            var overdue = await db.Invoices.AsNoTracking().Where(i => i.TenantId == tenant.TenantId && i.CustomerId == balance.CustomerId && i.CurrencyCode == balance.CurrencyCode
                && i.Status == InvoiceStatuses.Submitted && i.IsActive && i.DueDate != null && i.DueDate < today && i.BalanceAmount > 0).SumAsync(i => i.BalanceAmount, ct);

            rows.Add(new Dictionary<string, object?>
            {
                ["customerCode"] = customer?.CustomerCode, ["customerName"] = customer?.CustomerName, ["currencyCode"] = balance.CurrencyCode,
                ["totalDebits"] = totals?.Dr ?? 0m, ["totalCredits"] = totals?.Cr ?? 0m, ["balance"] = balance.BalanceAmount, ["overdue"] = overdue, ["lastPayment"] = lastPayment
            });
        }
        return (columns, rows);
    }

    // LED-04: Receivables Aging — customer's overdue invoices bucketed by days past due, as-of a date.
    private async Task<(List<string>, List<Dictionary<string, object?>>)> Led04Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "customerCode", "customerName", "current", "d1To30", "d31To60", "d61To90", "d91To180", "d180Plus", "total" };
        var asOf = f.AsOf ?? await clock.TodayAsync(tenant.TenantId);

        var invoices = await db.Invoices.AsNoTracking().Where(i => i.TenantId == tenant.TenantId && i.Status == InvoiceStatuses.Submitted && i.IsActive
            && i.BalanceAmount > 0 && i.DueDate != null && (f.CustomerId == null || i.CustomerId == f.CustomerId)).ToListAsync(ct);
        if (invoices.Count == 0) return (columns, []);

        var customerIds = invoices.Select(i => i.CustomerId).Distinct().ToList();
        var customers = await db.Customers.AsNoTracking().Where(c => c.TenantId == tenant.TenantId && customerIds.Contains(c.CustomerId)).ToDictionaryAsync(c => c.CustomerId, ct);

        var rows = invoices.GroupBy(i => i.CustomerId).Select(g =>
        {
            var customer = customers.GetValueOrDefault(g.Key);
            var buckets = new decimal[6];
            foreach (var i in g)
            {
                var days = asOf.DayNumber - i.DueDate!.Value.DayNumber;
                var bucket = days <= 0 ? 0 : days <= 30 ? 1 : days <= 60 ? 2 : days <= 90 ? 3 : days <= 180 ? 4 : 5;
                buckets[bucket] += i.BalanceAmount;
            }
            return new Dictionary<string, object?>
            {
                ["customerCode"] = customer?.CustomerCode, ["customerName"] = customer?.CustomerName, ["current"] = buckets[0],
                ["d1To30"] = buckets[1], ["d31To60"] = buckets[2], ["d61To90"] = buckets[3], ["d91To180"] = buckets[4], ["d180Plus"] = buckets[5],
                ["total"] = buckets.Sum()
            };
        }).ToList();
        return (columns, rows);
    }

    // LED-05: Aging Detail by Invoice — the same bucketing, one row per invoice.
    private async Task<(List<string>, List<Dictionary<string, object?>>)> Led05Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "invoiceNumber", "invoiceDate", "dueDate", "daysOverdue", "net", "paid", "balance", "bucket" };
        var asOf = f.AsOf ?? await clock.TodayAsync(tenant.TenantId);

        var invoices = await db.Invoices.AsNoTracking().Where(i => i.TenantId == tenant.TenantId && i.Status == InvoiceStatuses.Submitted && i.IsActive
            && i.BalanceAmount > 0 && i.DueDate != null && (f.CustomerId == null || i.CustomerId == f.CustomerId)).ToListAsync(ct);

        var rows = invoices.Select(i =>
        {
            var days = asOf.DayNumber - i.DueDate!.Value.DayNumber;
            var bucket = days <= 0 ? "Current" : days <= 30 ? "1-30" : days <= 60 ? "31-60" : days <= 90 ? "61-90" : days <= 180 ? "91-180" : "180+";
            return new Dictionary<string, object?>
            {
                ["invoiceNumber"] = i.InvoiceNumber, ["invoiceDate"] = i.InvoiceDate, ["dueDate"] = i.DueDate, ["daysOverdue"] = Math.Max(0, days),
                ["net"] = i.NetAmount, ["paid"] = i.PaidAmount, ["balance"] = i.BalanceAmount, ["bucket"] = bucket
            };
        }).ToList();
        return (columns, rows);
    }

    // LED-06: Receipts Register.
    private async Task<(List<string>, List<Dictionary<string, object?>>)> Led06Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "receiptNumber", "receiptDate", "customerCode", "method", "instrumentNo", "bankAccount", "amount", "status" };
        var receipts = await db.CustomerReceipts.AsNoTracking().Where(r => r.TenantId == tenant.TenantId
            && (f.From == null || r.ReceiptDate >= f.From) && (f.To == null || r.ReceiptDate <= f.To) && (f.CustomerId == null || r.CustomerId == f.CustomerId)
            && (string.IsNullOrEmpty(f.Method) || r.PaymentMethod == f.Method) && (f.BankCashAccountId == null || r.BankCashAccountId == f.BankCashAccountId))
            .ToListAsync(ct);
        if (receipts.Count == 0) return (columns, []);

        var customerIds = receipts.Select(r => r.CustomerId).Distinct().ToList();
        var customers = await db.Customers.AsNoTracking().Where(c => c.TenantId == tenant.TenantId && customerIds.Contains(c.CustomerId)).ToDictionaryAsync(c => c.CustomerId, c => c.CustomerCode, ct);
        var accountIds = receipts.Select(r => r.BankCashAccountId).Distinct().ToList();
        var accounts = await db.BankCashAccounts.AsNoTracking().Where(a => a.TenantId == tenant.TenantId && accountIds.Contains(a.BankCashAccountId)).ToDictionaryAsync(a => a.BankCashAccountId, a => a.AccountTitle, ct);

        var rows = receipts.Select(r => new Dictionary<string, object?>
        {
            ["receiptNumber"] = r.ReceiptNumber, ["receiptDate"] = r.ReceiptDate, ["customerCode"] = customers.GetValueOrDefault(r.CustomerId),
            ["method"] = r.PaymentMethod, ["instrumentNo"] = r.InstrumentNo, ["bankAccount"] = accounts.GetValueOrDefault(r.BankCashAccountId),
            ["amount"] = r.ReceiptAmount, ["status"] = r.Status
        }).ToList();
        return (columns, rows);
    }

    // LED-07: Payment Allocation Detail — receipt → invoice split.
    private async Task<(List<string>, List<Dictionary<string, object?>>)> Led07Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "receiptNumber", "invoiceNumber", "allocatedAmount", "ledgerEntryId" };
        var payments = await db.InvoicePayments.AsNoTracking().Where(p => p.TenantId == tenant.TenantId
            && (f.From == null || p.PaymentDate >= f.From) && (f.To == null || p.PaymentDate <= f.To)).ToListAsync(ct);
        if (payments.Count == 0) return (columns, []);

        var invoiceIds = payments.Select(p => p.InvoiceId).Distinct().ToList();
        var invoices = await db.Invoices.AsNoTracking().Where(i => i.TenantId == tenant.TenantId && invoiceIds.Contains(i.InvoiceId)
            && (f.CustomerId == null || i.CustomerId == f.CustomerId)).ToDictionaryAsync(i => i.InvoiceId, ct);
        var receiptIds = payments.Select(p => p.CustomerReceiptId).Distinct().ToList();
        var receipts = await db.CustomerReceipts.AsNoTracking().Where(r => r.TenantId == tenant.TenantId && receiptIds.Contains(r.CustomerReceiptId)).ToDictionaryAsync(r => r.CustomerReceiptId, r => r.ReceiptNumber, ct);

        var rows = payments.Where(p => invoices.ContainsKey(p.InvoiceId)).Select(p => new Dictionary<string, object?>
        {
            ["receiptNumber"] = receipts.GetValueOrDefault(p.CustomerReceiptId), ["invoiceNumber"] = invoices[p.InvoiceId].InvoiceNumber,
            ["allocatedAmount"] = p.Amount, ["ledgerEntryId"] = p.LedgerEntryId
        }).ToList();
        return (columns, rows);
    }

    // LED-08: Payment Reversals.
    private async Task<(List<string>, List<Dictionary<string, object?>>)> Led08Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "receiptNumber", "invoiceNumber", "amount", "reversalDate", "reason", "by" };
        var reversed = await db.InvoicePayments.AsNoTracking().Where(p => p.TenantId == tenant.TenantId && p.Status == InvoicePaymentRowStatuses.Reversed
            && (f.From == null || p.ReversedOn >= f.From.Value.ToDateTime(TimeOnly.MinValue)) && (f.To == null || p.ReversedOn <= f.To.Value.ToDateTime(TimeOnly.MaxValue))).ToListAsync(ct);
        if (reversed.Count == 0) return (columns, []);

        var invoiceIds = reversed.Select(p => p.InvoiceId).Distinct().ToList();
        var invoices = await db.Invoices.AsNoTracking().Where(i => i.TenantId == tenant.TenantId && invoiceIds.Contains(i.InvoiceId)).ToDictionaryAsync(i => i.InvoiceId, ct);
        var receiptIds = reversed.Select(p => p.CustomerReceiptId).Distinct().ToList();
        var receipts = await db.CustomerReceipts.AsNoTracking().Where(r => r.TenantId == tenant.TenantId && receiptIds.Contains(r.CustomerReceiptId)).ToDictionaryAsync(r => r.CustomerReceiptId, r => r.ReceiptNumber, ct);

        var rows = reversed.Select(p => new Dictionary<string, object?>
        {
            ["receiptNumber"] = receipts.GetValueOrDefault(p.CustomerReceiptId), ["invoiceNumber"] = invoices.GetValueOrDefault(p.InvoiceId)?.InvoiceNumber,
            ["amount"] = p.Amount, ["reversalDate"] = p.ReversedOn, ["reason"] = p.ReversalReason, ["by"] = p.ReversedBy
        }).ToList();
        return (columns, rows);
    }

    // LED-09: Customer Credit Balances — negative-balance active invoices.
    private async Task<(List<string>, List<Dictionary<string, object?>>)> Led09Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "customerCode", "invoiceNumber", "creditAmount", "origin", "ageDays" };
        var asOf = f.AsOf ?? await clock.TodayAsync(tenant.TenantId);

        var invoices = await db.Invoices.AsNoTracking().Where(i => i.TenantId == tenant.TenantId && i.Status == InvoiceStatuses.Submitted && i.IsActive
            && i.BalanceAmount < 0 && (f.CustomerId == null || i.CustomerId == f.CustomerId)).ToListAsync(ct);
        if (invoices.Count == 0) return (columns, []);

        var customerIds = invoices.Select(i => i.CustomerId).Distinct().ToList();
        var customers = await db.Customers.AsNoTracking().Where(c => c.TenantId == tenant.TenantId && customerIds.Contains(c.CustomerId)).ToDictionaryAsync(c => c.CustomerId, c => c.CustomerCode, ct);

        var rows = invoices.Select(i => new Dictionary<string, object?>
        {
            ["customerCode"] = customers.GetValueOrDefault(i.CustomerId), ["invoiceNumber"] = i.InvoiceNumber, ["creditAmount"] = -i.BalanceAmount,
            ["origin"] = i.TransferredInAmount > 0 ? "Transfer" : "Overpayment", ["ageDays"] = asOf.DayNumber - i.InvoiceDate.DayNumber
        }).ToList();
        return (columns, rows);
    }

    // LED-10: Collections Report — cash collected in a period.
    private async Task<(List<string>, List<Dictionary<string, object?>>)> Led10Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "receiptDate", "customerCode", "receiptNumber", "method", "bankAccount", "amount" };
        var receipts = await db.CustomerReceipts.AsNoTracking().Where(r => r.TenantId == tenant.TenantId && r.Status == CustomerReceiptStatuses.Posted
            && (f.From == null || r.ReceiptDate >= f.From) && (f.To == null || r.ReceiptDate <= f.To) && (f.CustomerId == null || r.CustomerId == f.CustomerId)
            && (f.BankCashAccountId == null || r.BankCashAccountId == f.BankCashAccountId)).ToListAsync(ct);
        if (receipts.Count == 0) return (columns, []);

        var customerIds = receipts.Select(r => r.CustomerId).Distinct().ToList();
        var customers = await db.Customers.AsNoTracking().Where(c => c.TenantId == tenant.TenantId && customerIds.Contains(c.CustomerId)).ToDictionaryAsync(c => c.CustomerId, c => c.CustomerCode, ct);
        var accountIds = receipts.Select(r => r.BankCashAccountId).Distinct().ToList();
        var accounts = await db.BankCashAccounts.AsNoTracking().Where(a => a.TenantId == tenant.TenantId && accountIds.Contains(a.BankCashAccountId)).ToDictionaryAsync(a => a.BankCashAccountId, a => a.AccountTitle, ct);

        var rows = receipts.Select(r => new Dictionary<string, object?>
        {
            ["receiptDate"] = r.ReceiptDate, ["customerCode"] = customers.GetValueOrDefault(r.CustomerId), ["receiptNumber"] = r.ReceiptNumber,
            ["method"] = r.PaymentMethod, ["bankAccount"] = accounts.GetValueOrDefault(r.BankCashAccountId), ["amount"] = r.ReceiptAmount
        }).ToList();
        return (columns, rows);
    }

    // LED-11: Days Sales Outstanding — average days from invoice date to fully-Paid, per customer.
    private async Task<(List<string>, List<Dictionary<string, object?>>)> Led11Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "customerCode", "avgDaysToPay", "dso" };
        var paidInvoices = await db.Invoices.AsNoTracking().Where(i => i.TenantId == tenant.TenantId && i.PaymentStatus == InvoicePaymentStatuses.Paid
            && (f.From == null || i.InvoiceDate >= f.From) && (f.To == null || i.InvoiceDate <= f.To) && (f.CustomerId == null || i.CustomerId == f.CustomerId)).ToListAsync(ct);
        if (paidInvoices.Count == 0) return (columns, []);

        var invoiceIds = paidInvoices.Select(i => i.InvoiceId).ToList();
        var lastSettlingEntry = await db.CustomerLedgerEntries.AsNoTracking()
            .Where(e => e.TenantId == tenant.TenantId && e.InvoiceId != null && invoiceIds.Contains(e.InvoiceId!.Value)
                && (e.EntryType == LedgerEntryTypes.Payment || e.EntryType == LedgerEntryTypes.WriteOff || e.EntryType == LedgerEntryTypes.Discount || e.EntryType == LedgerEntryTypes.TransferIn))
            .GroupBy(e => e.InvoiceId!.Value).Select(g => new { InvoiceId = g.Key, LastDate = g.Max(e => e.EntryDate) }).ToDictionaryAsync(g => g.InvoiceId, g => g.LastDate, ct);

        var customerIds = paidInvoices.Select(i => i.CustomerId).Distinct().ToList();
        var customers = await db.Customers.AsNoTracking().Where(c => c.TenantId == tenant.TenantId && customerIds.Contains(c.CustomerId)).ToDictionaryAsync(c => c.CustomerId, c => c.CustomerCode, ct);

        var rows = paidInvoices.Where(i => lastSettlingEntry.ContainsKey(i.InvoiceId)).GroupBy(i => i.CustomerId).Select(g =>
        {
            var days = g.Select(i => (double)(lastSettlingEntry[i.InvoiceId].DayNumber - i.InvoiceDate.DayNumber)).Where(d => d >= 0).ToList();
            var avg = days.Count == 0 ? 0 : days.Average();
            return new Dictionary<string, object?> { ["customerCode"] = customers.GetValueOrDefault(g.Key), ["avgDaysToPay"] = Math.Round(avg, 1), ["dso"] = Math.Round(avg, 1) };
        }).ToList();
        return (columns, rows);
    }

    // LED-12: Ledger Reconciliation Exceptions — CC-39's own latest run, read straight through.
    private async Task<(List<string>, List<Dictionary<string, object?>>)> Led12Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "invoiceNumber", "invoiceBalance", "ledgerBalance", "difference" };
        var latest = await reconciliation.GetLatestAsync(ct);
        if (latest is null) return (columns, []);

        var rows = latest.Mismatches.Where(m => f.InvoiceId == null || m.InvoiceId == f.InvoiceId).Select(m => new Dictionary<string, object?>
        {
            ["invoiceNumber"] = m.InvoiceNumber, ["invoiceBalance"] = m.InvoiceBalance, ["ledgerBalance"] = m.LedgerBalance, ["difference"] = m.Difference
        }).ToList();
        return (columns, rows);
    }

    // LED-13: Carry Forward & Refunds.
    private async Task<(List<string>, List<Dictionary<string, object?>>)> Led13Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "date", "customerCode", "sourceInvoice", "targetOrRefund", "amount", "reason" };
        var carryForwards = await db.InvoiceCreditCarryForwards.AsNoTracking().Where(c => c.TenantId == tenant.TenantId
            && (f.From == null || c.CarryForwardDate >= f.From) && (f.To == null || c.CarryForwardDate <= f.To)).ToListAsync(ct);
        var refunds = await db.CustomerRefunds.AsNoTracking().Where(r => r.TenantId == tenant.TenantId
            && (f.From == null || r.RefundDate >= f.From) && (f.To == null || r.RefundDate <= f.To)).ToListAsync(ct);

        var invoiceIds = carryForwards.SelectMany(c => new[] { c.SourceInvoiceId, c.TargetInvoiceId }).Concat(refunds.Select(r => r.InvoiceId)).Distinct().ToList();
        var invoices = invoiceIds.Count == 0 ? new Dictionary<long, Invoice>() : await db.Invoices.AsNoTracking()
            .Where(i => i.TenantId == tenant.TenantId && invoiceIds.Contains(i.InvoiceId)).ToDictionaryAsync(i => i.InvoiceId, ct);
        var custByInvoice = invoices.Values.ToDictionary(i => i.InvoiceId, i => i.CustomerCode);

        var rows = new List<Dictionary<string, object?>>();
        foreach (var c in carryForwards)
        {
            if (f.CustomerId is { } cid && invoices.GetValueOrDefault(c.SourceInvoiceId)?.CustomerId != cid) continue;
            rows.Add(new Dictionary<string, object?>
            {
                ["date"] = c.CarryForwardDate, ["customerCode"] = custByInvoice.GetValueOrDefault(c.SourceInvoiceId),
                ["sourceInvoice"] = invoices.GetValueOrDefault(c.SourceInvoiceId)?.InvoiceNumber,
                ["targetOrRefund"] = $"Carry forward to {invoices.GetValueOrDefault(c.TargetInvoiceId)?.InvoiceNumber}", ["amount"] = c.Amount, ["reason"] = c.Reason
            });
        }
        foreach (var r in refunds)
        {
            if (f.CustomerId is { } cid && invoices.GetValueOrDefault(r.InvoiceId)?.CustomerId != cid) continue;
            rows.Add(new Dictionary<string, object?>
            {
                ["date"] = r.RefundDate, ["customerCode"] = custByInvoice.GetValueOrDefault(r.InvoiceId),
                ["sourceInvoice"] = invoices.GetValueOrDefault(r.InvoiceId)?.InvoiceNumber, ["targetOrRefund"] = "Refund", ["amount"] = r.Amount, ["reason"] = r.Reason
            });
        }
        return (columns, rows.OrderBy(r => r["date"]).ToList());
    }

    // LED-14: Advance Payments (Open trips).
    private async Task<(List<string>, List<Dictionary<string, object?>>)> Led14Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "advanceNumber", "advanceDate", "customerCode", "tripNumber", "amount", "applied", "refunded", "unapplied", "invoiceAppliedTo", "status" };
        var advances = await db.CustomerAdvances.AsNoTracking().Where(a => a.TenantId == tenant.TenantId
            && (f.From == null || a.AdvanceDate >= f.From) && (f.To == null || a.AdvanceDate <= f.To) && (f.CustomerId == null || a.CustomerId == f.CustomerId)
            && (string.IsNullOrEmpty(f.AdvanceStatus) || a.Status == f.AdvanceStatus)).ToListAsync(ct);
        if (advances.Count == 0) return (columns, []);

        var customerIds = advances.Select(a => a.CustomerId).Distinct().ToList();
        var customers = await db.Customers.AsNoTracking().Where(c => c.TenantId == tenant.TenantId && customerIds.Contains(c.CustomerId)).ToDictionaryAsync(c => c.CustomerId, c => c.CustomerCode, ct);
        var tripIds = advances.Select(a => a.TripId).Distinct().ToList();
        var trips = await db.Trips.AsNoTracking().Where(t => t.TenantId == tenant.TenantId && tripIds.Contains(t.TripId)).ToDictionaryAsync(t => t.TripId, t => t.TripNumber, ct);
        var advanceIds = advances.Select(a => a.CustomerAdvanceId).ToList();
        var appliedIn = await db.CustomerLedgerEntries.AsNoTracking().Where(e => e.TenantId == tenant.TenantId && e.SourceType == LedgerSourceTypes.CustomerAdvance
            && advanceIds.Contains(e.SourceId) && e.EntryType == LedgerEntryTypes.AdvanceApplyIn).ToListAsync(ct);
        var invoiceIds = appliedIn.Where(e => e.InvoiceId != null).Select(e => e.InvoiceId!.Value).Distinct().ToList();
        var invoiceNumbers = invoiceIds.Count == 0 ? new Dictionary<long, string>() : await db.Invoices.AsNoTracking()
            .Where(i => i.TenantId == tenant.TenantId && invoiceIds.Contains(i.InvoiceId)).ToDictionaryAsync(i => i.InvoiceId, i => i.InvoiceNumber, ct);
        var appliedToByAdvance = appliedIn.Where(e => e.InvoiceId != null).ToDictionary(e => e.SourceId, e => invoiceNumbers.GetValueOrDefault(e.InvoiceId!.Value));

        var rows = advances.Select(a => new Dictionary<string, object?>
        {
            ["advanceNumber"] = a.AdvanceNumber, ["advanceDate"] = a.AdvanceDate, ["customerCode"] = customers.GetValueOrDefault(a.CustomerId),
            ["tripNumber"] = trips.GetValueOrDefault(a.TripId), ["amount"] = a.Amount, ["applied"] = a.AppliedAmount, ["refunded"] = a.RefundedAmount,
            ["unapplied"] = a.Amount - a.AppliedAmount - a.RefundedAmount, ["invoiceAppliedTo"] = appliedToByAdvance.GetValueOrDefault(a.CustomerAdvanceId), ["status"] = a.Status
        }).ToList();
        return (columns, rows);
    }

    // LED-15: Write-offs & Discounts.
    private async Task<(List<string>, List<Dictionary<string, object?>>)> Led15Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "date", "customerCode", "invoiceNumber", "type", "amount", "reason", "by", "reversed" };
        var settlements = await db.InvoiceSettlements.AsNoTracking().Where(s => s.TenantId == tenant.TenantId
            && (f.From == null || s.SettlementDate >= f.From) && (f.To == null || s.SettlementDate <= f.To)
            && (string.IsNullOrEmpty(f.SettlementType) || s.SettlementType == f.SettlementType)).ToListAsync(ct);
        if (settlements.Count == 0) return (columns, []);

        var invoiceIds = settlements.Select(s => s.InvoiceId).Distinct().ToList();
        var invoices = await db.Invoices.AsNoTracking().Where(i => i.TenantId == tenant.TenantId && invoiceIds.Contains(i.InvoiceId)
            && (f.CustomerId == null || i.CustomerId == f.CustomerId)).ToDictionaryAsync(i => i.InvoiceId, ct);

        var rows = settlements.Where(s => invoices.ContainsKey(s.InvoiceId)).Select(s => new Dictionary<string, object?>
        {
            ["date"] = s.SettlementDate, ["customerCode"] = invoices[s.InvoiceId].CustomerCode, ["invoiceNumber"] = invoices[s.InvoiceId].InvoiceNumber,
            ["type"] = s.SettlementType, ["amount"] = s.Amount, ["reason"] = s.Reason, ["by"] = s.CreatedBy, ["reversed"] = s.Status == InvoiceSettlementStatuses.Reversed
        }).ToList();
        return (columns, rows);
    }
}
