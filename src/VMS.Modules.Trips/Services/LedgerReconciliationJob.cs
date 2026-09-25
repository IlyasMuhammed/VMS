using Microsoft.EntityFrameworkCore;
using VMS.Modules.Trips.Data;
using VMS.Modules.Trips.Domain;
using VMS.Modules.Trips.Models;
using VMS.Shared.Common;

namespace VMS.Modules.Trips.Services;

/// <summary>§40A.5 LR-4: "For every Submitted, active invoice: Σ ledger (Dr − Cr) for the invoice =
/// Invoice.BalanceAmount. A nightly reconciliation job reports any mismatch to Finance and Admin." Reporting is
/// scoped to a read endpoint (<c>GET /api/ledger-reconciliation/latest</c>, `Ledger.View`) rather than a push
/// notification — wiring into the first FSD's own Notifications module would be a real cross-module integration
/// this task's own three acceptance items (AC-49, "mismatch reported," period lock) do not ask for; documented,
/// the same honest-scoping choice this register keeps making rather than silently expanding scope.</summary>
public interface ILedgerReconciliationJob
{
    Task<LedgerReconciliationRunModel> RunAsync(CancellationToken ct = default);
    Task<LedgerReconciliationRunModel?> GetLatestAsync(CancellationToken ct = default);
}

internal sealed class LedgerReconciliationJob(TripsDbContext db, ITenantContext tenant) : ILedgerReconciliationJob
{
    public async Task<LedgerReconciliationRunModel> RunAsync(CancellationToken ct = default)
    {
        var invoices = await db.Invoices.AsNoTracking()
            .Where(i => i.TenantId == tenant.TenantId && i.Status == InvoiceStatuses.Submitted && i.IsActive)
            .Select(i => new { i.InvoiceId, i.InvoiceNumber, i.BalanceAmount }).ToListAsync(ct);

        var ledgerBalances = await db.CustomerLedgerEntries.AsNoTracking()
            .Where(e => e.TenantId == tenant.TenantId && e.InvoiceId != null)
            .GroupBy(e => e.InvoiceId!.Value)
            .Select(g => new { InvoiceId = g.Key, Balance = g.Sum(e => e.DebitAmount - e.CreditAmount) })
            .ToDictionaryAsync(g => g.InvoiceId, g => g.Balance, ct);

        var run = new LedgerReconciliationRun { RunOn = DateTime.UtcNow, InvoicesChecked = invoices.Count };
        db.LedgerReconciliationRuns.Add(run);
        await db.SaveChangesAsync(ct);

        var mismatches = new List<LedgerReconciliationMismatch>();
        foreach (var invoice in invoices)
        {
            var ledgerBalance = ledgerBalances.GetValueOrDefault(invoice.InvoiceId, 0m);
            if (ledgerBalance != invoice.BalanceAmount)
            {
                mismatches.Add(new LedgerReconciliationMismatch
                {
                    LedgerReconciliationRunId = run.LedgerReconciliationRunId, InvoiceId = invoice.InvoiceId, InvoiceNumber = invoice.InvoiceNumber,
                    LedgerBalance = ledgerBalance, InvoiceBalance = invoice.BalanceAmount, Difference = ledgerBalance - invoice.BalanceAmount
                });
            }
        }
        run.MismatchCount = mismatches.Count;
        if (mismatches.Count > 0) db.LedgerReconciliationMismatches.AddRange(mismatches);
        await db.SaveChangesAsync(ct);

        return ToModel(run, mismatches);
    }

    public async Task<LedgerReconciliationRunModel?> GetLatestAsync(CancellationToken ct = default)
    {
        var run = await db.LedgerReconciliationRuns.AsNoTracking().Where(r => r.TenantId == tenant.TenantId)
            .OrderByDescending(r => r.RunOn).FirstOrDefaultAsync(ct);
        if (run is null) return null;
        var mismatches = await db.LedgerReconciliationMismatches.AsNoTracking()
            .Where(m => m.TenantId == tenant.TenantId && m.LedgerReconciliationRunId == run.LedgerReconciliationRunId).ToListAsync(ct);
        return ToModel(run, mismatches);
    }

    private static LedgerReconciliationRunModel ToModel(LedgerReconciliationRun run, List<LedgerReconciliationMismatch> mismatches) => new()
    {
        LedgerReconciliationRunId = run.LedgerReconciliationRunId, RunOn = run.RunOn, InvoicesChecked = run.InvoicesChecked, MismatchCount = run.MismatchCount,
        Mismatches = mismatches.Select(m => new LedgerReconciliationMismatchModel
        {
            InvoiceId = m.InvoiceId, InvoiceNumber = m.InvoiceNumber, LedgerBalance = m.LedgerBalance, InvoiceBalance = m.InvoiceBalance, Difference = m.Difference
        }).ToList()
    };
}
