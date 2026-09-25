using Microsoft.EntityFrameworkCore;
using VMS.Modules.Trips.Data;
using VMS.Modules.Trips.Domain;
using VMS.Modules.Trips.Models;
using VMS.Shared.Common;
using VMS.Shared.Exceptions;
using VMS.Shared.Tenancy;
using VMS.Shared.Time;

namespace VMS.Modules.Trips.Services;

/// <summary>§40A.4: the three read screens over the append-only ledger CC-30 built — Customer Ledger (statement),
/// the Invoice Ledger tab, and Customer Balances. Purely additive reads; no entry is ever posted here.</summary>
public interface ICustomerLedgerReadService
{
    Task<CustomerLedgerStatementModel> GetStatementAsync(int customerId, DateOnly? from, DateOnly? to, long? invoiceId, string? currencyCode,
        bool includeReversedPairs, CancellationToken ct = default);

    Task<InvoiceLedgerModel> GetInvoiceLedgerAsync(long invoiceId, CancellationToken ct = default);

    Task<IReadOnlyList<CustomerBalanceModel>> GetCustomerBalancesAsync(int customerId, CancellationToken ct = default);

    Task<IReadOnlyList<CustomerBalanceSummaryModel>> ListBalancesAsync(CancellationToken ct = default);

    /// <summary>§40A.4: "Export PDF (customer statement format with company header)."</summary>
    Task<(byte[] Bytes, string FileName)> ExportStatementPdfAsync(int customerId, DateOnly? from, DateOnly? to, string? currencyCode, CancellationToken ct = default);
}

internal sealed class CustomerLedgerReadService(
    TripsDbContext db, ITenantContext tenant, IOperatingClock clock, ICustomerLedgerPostingService ledger,
    ITenantProfileDirectory tenantProfile, ICustomerStatementPdfRenderer pdf) : ICustomerLedgerReadService
{
    public async Task<CustomerLedgerStatementModel> GetStatementAsync(int customerId, DateOnly? from, DateOnly? to, long? invoiceId, string? currencyCode,
        bool includeReversedPairs, CancellationToken ct = default)
    {
        var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.TenantId == tenant.TenantId && c.CustomerId == customerId, ct)
            ?? throw new NotFoundException($"Customer {customerId} was not found.");
        var currency = string.IsNullOrWhiteSpace(currencyCode) ? customer.CurrencyCode : currencyCode;

        var all = await db.CustomerLedgerEntries.AsNoTracking()
            .Where(e => e.TenantId == tenant.TenantId && e.CustomerId == customerId && e.CurrencyCode == currency)
            .OrderBy(e => e.EntryDate).ThenBy(e => e.CustomerSeq).ToListAsync(ct);

        if (!includeReversedPairs)
        {
            // §40A.4: "Include reversed pairs (default on)" — off hides both an entry that was itself reversed
            // and the reversal entry that cancelled it, so a clean read shows only what is still standing.
            var reversedAway = all.Where(e => e.ReversesEntryId is not null).Select(e => e.ReversesEntryId!.Value).ToHashSet();
            var reversals = all.Where(e => e.ReversesEntryId is not null).Select(e => e.CustomerLedgerEntryId).ToHashSet();
            all = all.Where(e => !reversedAway.Contains(e.CustomerLedgerEntryId) && !reversals.Contains(e.CustomerLedgerEntryId)).ToList();
        }

        var opening = from is null ? 0 : all.Where(e => e.EntryDate < from).Sum(e => e.DebitAmount - e.CreditAmount);
        var period = all.Where(e => (from is null || e.EntryDate >= from) && (to is null || e.EntryDate <= to)).ToList();

        var invoiceIds = period.Where(e => e.InvoiceId is not null).Select(e => e.InvoiceId!.Value).Distinct().ToList();
        var invoiceNumbers = invoiceIds.Count == 0 ? new Dictionary<long, string>() : await db.Invoices.AsNoTracking()
            .Where(i => i.TenantId == tenant.TenantId && invoiceIds.Contains(i.InvoiceId)).ToDictionaryAsync(i => i.InvoiceId, i => i.InvoiceNumber, ct);

        var running = opening;
        var rows = new List<CustomerLedgerStatementRowModel>();
        foreach (var e in period)
        {
            running += e.DebitAmount - e.CreditAmount;
            if (invoiceId is not null && e.InvoiceId != invoiceId) continue;
            rows.Add(new CustomerLedgerStatementRowModel
            {
                CustomerLedgerEntryId = e.CustomerLedgerEntryId, EntryNumber = e.EntryNumber, EntryDate = e.EntryDate, EntryType = e.EntryType,
                InvoiceId = e.InvoiceId, InvoiceNumber = e.InvoiceId is { } id ? invoiceNumbers.GetValueOrDefault(id) : null,
                DocumentNo = e.DocumentNo, Narration = e.Narration, DebitAmount = e.DebitAmount, CreditAmount = e.CreditAmount, RunningBalance = running
            });
        }

        var periodDebits = period.Sum(e => e.DebitAmount);
        var periodCredits = period.Sum(e => e.CreditAmount);
        var overdueAmount = await OverdueAmountAsync(customerId, currency, ct);

        return new CustomerLedgerStatementModel
        {
            CustomerId = customerId, CustomerCode = customer.CustomerCode, CustomerName = customer.CustomerName, CurrencyCode = currency,
            From = from, To = to, OpeningBalance = opening, PeriodDebits = periodDebits, PeriodCredits = periodCredits,
            ClosingBalance = opening + periodDebits - periodCredits, OverdueAmount = overdueAmount, Rows = rows
        };
    }

    public async Task<InvoiceLedgerModel> GetInvoiceLedgerAsync(long invoiceId, CancellationToken ct = default)
    {
        var invoice = await db.Invoices.AsNoTracking().FirstOrDefaultAsync(i => i.TenantId == tenant.TenantId && i.InvoiceId == invoiceId, ct)
            ?? throw new NotFoundException($"Invoice {invoiceId} was not found.");
        var entries = await ledger.ListForInvoiceAsync(invoiceId, ct);
        var ledgerBalance = entries.Sum(e => e.DebitAmount - e.CreditAmount);

        return new InvoiceLedgerModel
        {
            InvoiceId = invoice.InvoiceId, InvoiceNumber = invoice.InvoiceNumber, InvoiceBalance = invoice.BalanceAmount,
            LedgerBalance = ledgerBalance, Reconciled = ledgerBalance == invoice.BalanceAmount, Entries = entries
        };
    }

    public async Task<IReadOnlyList<CustomerBalanceModel>> GetCustomerBalancesAsync(int customerId, CancellationToken ct = default)
    {
        var balances = await db.CustomerBalances.AsNoTracking().Where(b => b.TenantId == tenant.TenantId && b.CustomerId == customerId).ToListAsync(ct);
        return balances.Select(b => new CustomerBalanceModel { CustomerId = b.CustomerId, CurrencyCode = b.CurrencyCode, BalanceAmount = b.BalanceAmount }).ToList();
    }

    public async Task<IReadOnlyList<CustomerBalanceSummaryModel>> ListBalancesAsync(CancellationToken ct = default)
    {
        var balances = await db.CustomerBalances.AsNoTracking().Where(b => b.TenantId == tenant.TenantId).ToListAsync(ct);
        if (balances.Count == 0) return [];

        var customerIds = balances.Select(b => b.CustomerId).Distinct().ToList();
        var customers = await db.Customers.AsNoTracking().Where(c => c.TenantId == tenant.TenantId && customerIds.Contains(c.CustomerId))
            .ToDictionaryAsync(c => c.CustomerId, ct);

        var results = new List<CustomerBalanceSummaryModel>();
        foreach (var balance in balances)
        {
            var customer = customers.GetValueOrDefault(balance.CustomerId);
            var lastInvoiceDate = await db.Invoices.AsNoTracking()
                .Where(i => i.TenantId == tenant.TenantId && i.CustomerId == balance.CustomerId && i.CurrencyCode == balance.CurrencyCode && i.Status == InvoiceStatuses.Submitted)
                .OrderByDescending(i => i.InvoiceDate).Select(i => (DateOnly?)i.InvoiceDate).FirstOrDefaultAsync(ct);
            var lastPaymentDate = await db.CustomerLedgerEntries.AsNoTracking()
                .Where(e => e.TenantId == tenant.TenantId && e.CustomerId == balance.CustomerId && e.CurrencyCode == balance.CurrencyCode && e.EntryType == LedgerEntryTypes.Payment)
                .OrderByDescending(e => e.EntryDate).Select(e => (DateOnly?)e.EntryDate).FirstOrDefaultAsync(ct);

            results.Add(new CustomerBalanceSummaryModel
            {
                CustomerId = balance.CustomerId, CustomerCode = customer?.CustomerCode ?? string.Empty, CustomerName = customer?.CustomerName ?? string.Empty,
                CurrencyCode = balance.CurrencyCode, BalanceAmount = balance.BalanceAmount,
                OverdueAmount = await OverdueAmountAsync(balance.CustomerId, balance.CurrencyCode, ct),
                CreditAmount = balance.BalanceAmount < 0 ? -balance.BalanceAmount : 0, LastPaymentDate = lastPaymentDate, LastInvoiceDate = lastInvoiceDate
            });
        }
        return results;
    }

    public async Task<(byte[] Bytes, string FileName)> ExportStatementPdfAsync(int customerId, DateOnly? from, DateOnly? to, string? currencyCode, CancellationToken ct = default)
    {
        var statement = await GetStatementAsync(customerId, from, to, null, currencyCode, includeReversedPairs: true, ct);
        var profile = await tenantProfile.FindAsync(tenant.TenantId, ct);

        var data = new CustomerStatementPdfData(
            profile?.Name ?? string.Empty, profile?.Address, profile?.ContactEmail, profile?.ContactPhone,
            statement.CustomerCode, statement.CustomerName, statement.CurrencyCode, statement.From, statement.To,
            statement.OpeningBalance, statement.PeriodDebits, statement.PeriodCredits, statement.ClosingBalance, statement.OverdueAmount,
            statement.Rows.Select(r => new CustomerStatementPdfRow(r.EntryDate, r.EntryNumber, r.DocumentNo, r.EntryType, r.InvoiceNumber, r.Narration, r.DebitAmount, r.CreditAmount, r.RunningBalance)).ToList());

        var bytes = pdf.Render(data);
        return (bytes, $"{statement.CustomerCode}-statement.pdf");
    }

    /// <summary>Σ balance of every Active Submitted invoice past its own due date — §40A.4's own header field,
    /// finally exercising the <c>Invoice.DueDate</c> column this task went back and started stamping.</summary>
    private async Task<decimal> OverdueAmountAsync(int customerId, string currencyCode, CancellationToken ct)
    {
        var today = await clock.TodayAsync(tenant.TenantId);
        return await db.Invoices.AsNoTracking().Where(i => i.TenantId == tenant.TenantId && i.CustomerId == customerId && i.CurrencyCode == currencyCode
            && i.Status == InvoiceStatuses.Submitted && i.IsActive && i.DueDate != null && i.DueDate < today && i.BalanceAmount > 0)
            .SumAsync(i => i.BalanceAmount, ct);
    }
}
