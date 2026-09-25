using Microsoft.EntityFrameworkCore;
using VMS.Modules.Trips.Models;
using VMS.Shared.Auditing;

namespace VMS.Modules.Trips.Services;

/// <summary>§42.7: AUD-01..03. Reads the shared, Core-owned <c>core.AuditEntries</c> table this module's own
/// <c>TripsDbContext</c> already maps for writing (<c>InvoiceSubmissionService.StaleAsync</c> already reads it the
/// same way) — every module writes to one table so a history screen or export sees one root's full trail with a
/// single query, exactly as its own doc comment says.</summary>
internal sealed partial class LedgerReportService
{
    /// <summary>AUD-02's own "Rates, invoices, payments, ledger actions only" — every entity this module's own
    /// billing/ledger cluster writes, an approximate but reasonable reading of the FSD's own vague allowlist.</summary>
    private static readonly HashSet<string> FinancialEntities =
    [
        "Invoice", "TripRate", "TripConfiguration", "InvoicePayment", "InvoiceSettlement", "CustomerAdvance",
        "InvoicePaymentTransfer", "InvoiceCreditCarryForward", "CustomerRefund", "CustomerLedgerEntry", "Customer"
    ];

    private IQueryable<AuditEntry> FilteredAudit(ReportFilter f) => db.Set<AuditEntry>().AsNoTracking().Where(a => a.TenantId == tenant.TenantId
        && (f.From == null || a.OccurredAt >= f.From.Value.ToDateTime(TimeOnly.MinValue)) && (f.To == null || a.OccurredAt <= f.To.Value.ToDateTime(TimeOnly.MaxValue))
        && (string.IsNullOrEmpty(f.Method) || a.Entity == f.Method));

    private async Task<(List<string>, List<Dictionary<string, object?>>)> Aud01Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "occurredAt", "userName", "entity", "recordId", "action", "field", "oldValue", "newValue", "reason" };
        var entries = await FilteredAudit(f).OrderByDescending(a => a.OccurredAt).Take(2000).ToListAsync(ct);
        var rows = entries.Select(ToAuditRow).ToList();
        return (columns, rows);
    }

    private async Task<(List<string>, List<Dictionary<string, object?>>)> Aud02Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "occurredAt", "userName", "entity", "recordId", "action", "field", "oldValue", "newValue", "reason" };
        var entries = await FilteredAudit(f).Where(a => FinancialEntities.Contains(a.Entity) || (a.RootEntity != null && FinancialEntities.Contains(a.RootEntity)))
            .OrderByDescending(a => a.OccurredAt).Take(2000).ToListAsync(ct);
        var rows = entries.Select(ToAuditRow).ToList();
        return (columns, rows);
    }

    private static Dictionary<string, object?> ToAuditRow(AuditEntry a) => new()
    {
        ["occurredAt"] = a.OccurredAt, ["userName"] = a.UserName ?? "System", ["entity"] = a.Entity, ["recordId"] = a.RecordId,
        ["action"] = a.Action, ["field"] = a.Field, ["oldValue"] = a.OldValue, ["newValue"] = a.NewValue, ["reason"] = a.Reason
    };

    // AUD-03: "last login" isn't tracked by this module's own audit trail (that's the Auth module's own concern,
    // out of scope for a cross-module read this one task doesn't need) — the latest audit activity is used as a
    // documented, honest proxy for it, the same "reasoned approximation, not silently guessed" pattern this
    // register applies whenever a report needs data from outside its own reach.
    private async Task<(List<string>, List<Dictionary<string, object?>>)> Aud03Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "userName", "actionCount", "lastActivity" };
        var entries = await FilteredAudit(f).Where(a => a.UserId != null).ToListAsync(ct);
        if (entries.Count == 0) return (columns, []);

        var rows = entries.GroupBy(a => a.UserName ?? a.UserId.ToString()).Select(g => new Dictionary<string, object?>
        { ["userName"] = g.Key, ["actionCount"] = g.Count(), ["lastActivity"] = g.Max(a => a.OccurredAt) }).ToList();
        return (columns, rows);
    }
}
