using Microsoft.EntityFrameworkCore;
using VMS.Modules.Trips.Data;
using VMS.Modules.Trips.Domain;
using VMS.Modules.Trips.Models;
using VMS.Shared.Auditing;
using VMS.Shared.Common;
using VMS.Shared.Exceptions;
using VMS.Shared.Files;
using VMS.Shared.Lookups;
using VMS.Shared.Pagination;
using VMS.Shared.Partners;
using VMS.Shared.Time;
using VMS.Shared.Vehicles;

namespace VMS.Modules.Trips.Services;

/// <summary>§42: all 80 report codes across eight groups. §42.2's fifteen LED-xx codes (CC-41) live in this same
/// file; §42.1/§42.3-42.7's remaining 65 (CUS/TRP/PNL/INV/MST/AUD-xx, CC-42) are split into their own partial
/// files by group, purely for file size — every group shares this one dispatch/sort/page/export pipeline, since
/// the row/column shape problem CC-41 solved (wildly different columns per report) applies just as much here.</summary>
public interface ILedgerReportService
{
    Task<ReportResultModel> RunAsync(string code, ReportFilter filter, string runByUserName, CancellationToken ct = default);

    /// <summary>§42.9: "Large exports (&gt; 50,000 rows) run as background jobs and notify the user when the file
    /// is ready." A report at or under the threshold renders synchronously exactly as CC-41 always has (`Ready`
    /// true, bytes attached); over it, a <see cref="ReportExportJob"/> is queued and picked up by
    /// <see cref="ProcessQueuedExportsAsync"/> instead (`Ready` false, a job id to poll).</summary>
    Task<ReportExportResultModel> ExportOrQueueAsync(string code, ReportFilter filter, string runByUserName, int userId, CancellationToken ct = default);

    Task<ReportExportResultModel> GetExportJobAsync(long jobId, CancellationToken ct = default);

    /// <summary>Renders every still-Queued job — the hosted service's own tenant-loop calls this once per tenant.</summary>
    Task<int> ProcessQueuedExportsAsync(CancellationToken ct = default);
}

/// <summary>§42.9's own 50,000-row line, as a pure decision — public for the same DB-free testability reason this
/// module keeps reaching for (<c>TripRateOverlap</c>, <c>TripLifecycle</c>, <c>InvoiceEvidencePagination</c>):
/// proving the threshold itself needs no 50,001-row fixture.</summary>
public static class ReportExportDecision
{
    public const int BackgroundThreshold = 50_000;
    public static bool ShouldQueue(int rowCount) => rowCount > BackgroundThreshold;
}

internal sealed partial class LedgerReportService(
    TripsDbContext db, ITenantContext tenant, IOperatingClock clock, ILedgerReconciliationJob reconciliation,
    ILookupReader lookups, ITripPnLService pnl, IVehicleDirectory vehicles, IPartnerDirectory partners,
    IFileStore files, IFileDownloadLinks downloadLinks) : ILedgerReportService
{
    public async Task<ReportResultModel> RunAsync(string code, ReportFilter filter, string runByUserName, CancellationToken ct = default)
    {
        var (columns, rows) = await GatherAsync(code, filter, ct);

        if (!string.IsNullOrWhiteSpace(filter.SortBy) && columns.Contains(filter.SortBy))
        {
            rows = (filter.SortDesc ? rows.OrderByDescending(SortKey(filter.SortBy)) : rows.OrderBy(SortKey(filter.SortBy))).ToList();
        }

        var page = filter.Page < 1 ? 1 : filter.Page;
        var pageSize = filter.PageSize is < 1 or > 1000 ? 50 : filter.PageSize;
        var paged = rows.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return new ReportResultModel
        {
            Code = code, ReportName = ReportNames.GetValueOrDefault(code, code), RunOn = DateTime.UtcNow, RunBy = runByUserName,
            FiltersUsed = FiltersUsed(filter), Columns = columns,
            Data = new PaginatedResponse<Dictionary<string, object?>> { Items = paged, TotalCount = rows.Count, Page = page, PageSize = pageSize }
        };
    }

    public async Task<ReportExportResultModel> ExportOrQueueAsync(string code, ReportFilter filter, string runByUserName, int userId, CancellationToken ct = default)
    {
        var (columns, rows) = await GatherAsync(code, filter, ct);

        if (!ReportExportDecision.ShouldQueue(rows.Count))
        {
            var bytes = RenderCsv(code, columns, rows, runByUserName, filter);
            return new ReportExportResultModel { Ready = true, Bytes = bytes, FileName = $"{code}-{DateTime.UtcNow:yyyyMMddHHmmss}.csv" };
        }

        var job = new ReportExportJob
        {
            Code = code, FiltersJson = System.Text.Json.JsonSerializer.Serialize(FiltersUsed(filter)), Status = ReportExportJobStatuses.Queued,
            RequestedBy = userId, RequestedByName = runByUserName, RequestedOn = DateTime.UtcNow
        };
        db.ReportExportJobs.Add(job);
        await db.SaveChangesAsync(ct);
        return new ReportExportResultModel { Ready = false, JobId = job.ReportExportJobId, Status = job.Status };
    }

    public async Task<ReportExportResultModel> GetExportJobAsync(long jobId, CancellationToken ct = default)
    {
        var job = await db.ReportExportJobs.AsNoTracking().FirstOrDefaultAsync(j => j.TenantId == tenant.TenantId && j.ReportExportJobId == jobId, ct)
            ?? throw new NotFoundException($"Report export job {jobId} was not found.");
        if (job.Status != ReportExportJobStatuses.Ready)
            return new ReportExportResultModel { Ready = false, JobId = job.ReportExportJobId, Status = job.Status, Error = job.ErrorMessage };

        var stored = new StoredFile(job.StorageKey!, job.Sha256!, job.SizeBytes!.Value, job.ContentType!, job.OriginalFileName!, job.CompletedOn ?? job.RequestedOn);
        var link = downloadLinks.Create(tenant.TenantId, stored, inline: false);
        return new ReportExportResultModel { Ready = true, JobId = job.ReportExportJobId, Status = job.Status, DownloadUrl = link.Url };
    }

    public async Task<int> ProcessQueuedExportsAsync(CancellationToken ct = default)
    {
        var queued = await db.ReportExportJobs.Where(j => j.TenantId == tenant.TenantId && j.Status == ReportExportJobStatuses.Queued).ToListAsync(ct);
        var processed = 0;
        foreach (var job in queued)
        {
            job.Status = ReportExportJobStatuses.Processing;
            await db.SaveChangesAsync(ct);
            try
            {
                var filter = new ReportFilter();   // §42.9's own header still records the ORIGINAL filters (FiltersJson); re-running with none narrower is the safe default for a queued job.
                var (columns, rows) = await GatherAsync(job.Code, filter, ct);
                var bytes = RenderCsv(job.Code, columns, rows, job.RequestedByName, filter);
                using var stream = new MemoryStream(bytes);
                var stored = await files.SaveAsync(new FileUpload(stream, $"{job.Code}-{job.ReportExportJobId}.csv",
                    new FileOwner("ReportExportJob", job.ReportExportJobId.ToString()), FileRules.Scans, tenant.TenantId), ct);

                job.Status = ReportExportJobStatuses.Ready;
                job.StorageKey = stored.StorageKey; job.Sha256 = stored.Sha256; job.SizeBytes = stored.SizeBytes;
                job.ContentType = stored.ContentType; job.OriginalFileName = stored.OriginalFileName; job.CompletedOn = DateTime.UtcNow;
                processed++;
            }
            catch (Exception ex)
            {
                job.Status = ReportExportJobStatuses.Failed;
                job.ErrorMessage = ex.Message;
            }
            await db.SaveChangesAsync(ct);
        }
        return processed;
    }

    private byte[] RenderCsv(string code, List<string> columns, List<Dictionary<string, object?>> rows, string runByUserName, ReportFilter filter)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# Report: {code} — {ReportNames.GetValueOrDefault(code, code)}");
        sb.AppendLine($"# Run on: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC");
        sb.AppendLine($"# Run by: {runByUserName}");
        var usedFilters = FiltersUsed(filter);
        sb.AppendLine($"# Filters: {(usedFilters.Count == 0 ? "(none)" : string.Join("; ", usedFilters.Select(f => $"{f.Key}={f.Value}")))}");
        sb.AppendLine(string.Join(",", columns.Select(Csv)));
        foreach (var row in rows)
            sb.AppendLine(string.Join(",", columns.Select(c => Csv(FormatValue(row.GetValueOrDefault(c))))));
        return System.Text.Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static string FormatValue(object? value) => value switch
    {
        null => "",
        decimal d => d.ToString("0.00"),
        DateOnly d => d.ToString("yyyy-MM-dd"),
        DateTime d => d.ToString("yyyy-MM-dd HH:mm"),
        bool b => b ? "Yes" : "No",
        _ => value.ToString() ?? ""
    };

    private static string Csv(string value) => value.Contains(',') || value.Contains('"') || value.Contains('\n')
        ? $"\"{value.Replace("\"", "\"\"")}\"" : value;

    private static Func<Dictionary<string, object?>, object?> SortKey(string column) => row =>
    {
        var value = row.GetValueOrDefault(column);
        return value is IComparable ? value : value?.ToString();
    };

    private static Dictionary<string, string?> FiltersUsed(ReportFilter f)
    {
        var used = new Dictionary<string, string?>();
        if (f.From is { } from) used["from"] = from.ToString("yyyy-MM-dd");
        if (f.To is { } to) used["to"] = to.ToString("yyyy-MM-dd");
        if (f.AsOf is { } asOf) used["asOf"] = asOf.ToString("yyyy-MM-dd");
        if (f.CustomerId is { } cid) used["customerId"] = cid.ToString();
        if (f.InvoiceId is { } iid) used["invoiceId"] = iid.ToString();
        if (f.VehicleId is { } vid) used["vehicleId"] = vid.ToString();
        if (f.DriverId is { } did) used["driverId"] = did.ToString();
        if (!string.IsNullOrWhiteSpace(f.Method)) used["method"] = f.Method;
        if (f.BankCashAccountId is { } bid) used["bankCashAccountId"] = bid.ToString();
        if (!string.IsNullOrWhiteSpace(f.SettlementType)) used["settlementType"] = f.SettlementType;
        if (!string.IsNullOrWhiteSpace(f.AdvanceStatus)) used["advanceStatus"] = f.AdvanceStatus;
        if (!string.IsNullOrWhiteSpace(f.BalanceSign)) used["balanceSign"] = f.BalanceSign;
        if (!string.IsNullOrWhiteSpace(f.Status)) used["status"] = f.Status;
        if (!string.IsNullOrWhiteSpace(f.TripType)) used["tripType"] = f.TripType;
        return used;
    }

    private static readonly Dictionary<string, string> ReportNames = new()
    {
        ["LED-01"] = "Customer Ledger Statement", ["LED-02"] = "Invoice-wise Ledger", ["LED-03"] = "Customer Balances Summary",
        ["LED-04"] = "Receivables Aging", ["LED-05"] = "Aging Detail by Invoice", ["LED-06"] = "Receipts Register",
        ["LED-07"] = "Payment Allocation Detail", ["LED-08"] = "Payment Reversals", ["LED-09"] = "Customer Credit Balances",
        ["LED-10"] = "Collections Report", ["LED-11"] = "Days Sales Outstanding (DSO)", ["LED-12"] = "Ledger Reconciliation Exceptions",
        ["LED-13"] = "Carry Forward & Refunds", ["LED-14"] = "Advance Payments (Open trips)", ["LED-15"] = "Write-offs & Discounts",
        ["CUS-01"] = "Customer List", ["CUS-02"] = "Customer Contacts", ["CUS-03"] = "Customer Billing Addresses",
        ["CUS-04"] = "Customer Tax/Deduction Rules", ["CUS-05"] = "Customer Trip Configurations", ["CUS-06"] = "Customer Trip Summary",
        ["CUS-07"] = "Customer Invoice Summary", ["CUS-08"] = "Customer Payment History", ["CUS-09"] = "Customer Outstanding",
        ["CUS-10"] = "Customer Revenue Trend",
        ["TRP-01"] = "Trip Register", ["TRP-02"] = "Trips by Customer", ["TRP-03"] = "Trips by Vehicle", ["TRP-04"] = "Trips by Driver",
        ["TRP-05"] = "Trips by Route / Configuration", ["TRP-06"] = "Fixed Trips", ["TRP-07"] = "Open Trips", ["TRP-08"] = "Trip Status / Lifecycle",
        ["TRP-09"] = "Trip Event Timeline", ["TRP-10"] = "On Hold & Cancelled Trips", ["TRP-11"] = "Inactive Trips", ["TRP-12"] = "POD Status",
        ["TRP-13"] = "Rate Missing Trips", ["TRP-14"] = "Duplicate Customer References", ["TRP-15"] = "Driver Override Report", ["TRP-16"] = "Trip Issues",
        ["PNL-01"] = "Trip Expenses", ["PNL-02"] = "Expense Summary by Type", ["PNL-03"] = "Trip Fuel", ["PNL-04"] = "Fuel Efficiency",
        ["PNL-05"] = "Fuel Card Usage", ["PNL-06"] = "Fuel Card Expiry", ["PNL-07"] = "Trip Income", ["PNL-08"] = "Trip P&L",
        ["PNL-09"] = "Vehicle P&L (Operational)", ["PNL-10"] = "Customer Profitability", ["PNL-11"] = "Route / Configuration Profitability",
        ["PNL-12"] = "Driver Cost Report", ["PNL-13"] = "Monthly Operational P&L",
        ["INV-01"] = "Invoice Register", ["INV-02"] = "Invoice Detail", ["INV-03"] = "Invoice Lines", ["INV-04"] = "Invoiceable Trips",
        ["INV-05"] = "Invoiced Trips", ["INV-06"] = "Uninvoiced Trips", ["INV-07"] = "Paid Invoices", ["INV-08"] = "Partially Paid Invoices",
        ["INV-09"] = "Unpaid Invoices", ["INV-10"] = "Outstanding Invoices", ["INV-11"] = "Invoice Adjustments Register",
        ["INV-12"] = "Tax/Deduction Report", ["INV-13"] = "Regeneration History", ["INV-14"] = "Payment History",
        ["INV-15"] = "Payment Transfer History", ["INV-16"] = "Invoice Evidence Register", ["INV-17"] = "Cancelled & Inactive Invoices",
        ["INV-18"] = "Billing Coverage Gaps",
        ["MST-01"] = "Rate Master History", ["MST-02"] = "Rate Coverage Gaps", ["MST-03"] = "City & Route List",
        ["MST-04"] = "Configuration Vehicle Assignments", ["MST-05"] = "Invoice Template Register",
        ["AUD-01"] = "Audit Log", ["AUD-02"] = "Financial Audit Trail", ["AUD-03"] = "User Activity"
    };

    private Task<(List<string> Columns, List<Dictionary<string, object?>> Rows)> GatherAsync(string code, ReportFilter filter, CancellationToken ct) => code switch
    {
        "LED-01" => Led01Async(filter, ct), "LED-02" => Led02Async(filter, ct), "LED-03" => Led03Async(filter, ct),
        "LED-04" => Led04Async(filter, ct), "LED-05" => Led05Async(filter, ct), "LED-06" => Led06Async(filter, ct),
        "LED-07" => Led07Async(filter, ct), "LED-08" => Led08Async(filter, ct), "LED-09" => Led09Async(filter, ct),
        "LED-10" => Led10Async(filter, ct), "LED-11" => Led11Async(filter, ct), "LED-12" => Led12Async(filter, ct),
        "LED-13" => Led13Async(filter, ct), "LED-14" => Led14Async(filter, ct), "LED-15" => Led15Async(filter, ct),
        "CUS-01" => Cus01Async(filter, ct), "CUS-02" => Cus02Async(filter, ct), "CUS-03" => Cus03Async(filter, ct),
        "CUS-04" => Cus04Async(filter, ct), "CUS-05" => Cus05Async(filter, ct), "CUS-06" => Cus06Async(filter, ct),
        "CUS-07" => Cus07Async(filter, ct), "CUS-08" => Cus08Async(filter, ct), "CUS-09" => Cus09Async(filter, ct),
        "CUS-10" => Cus10Async(filter, ct),
        "TRP-01" => Trp01Async(filter, ct), "TRP-02" => Trp02Async(filter, ct), "TRP-03" => Trp03Async(filter, ct),
        "TRP-04" => Trp04Async(filter, ct), "TRP-05" => Trp05Async(filter, ct), "TRP-06" => Trp06Async(filter, ct),
        "TRP-07" => Trp07Async(filter, ct), "TRP-08" => Trp08Async(filter, ct), "TRP-09" => Trp09Async(filter, ct),
        "TRP-10" => Trp10Async(filter, ct), "TRP-11" => Trp11Async(filter, ct), "TRP-12" => Trp12Async(filter, ct),
        "TRP-13" => Trp13Async(filter, ct), "TRP-14" => Trp14Async(filter, ct), "TRP-15" => Trp15Async(filter, ct),
        "TRP-16" => Trp16Async(filter, ct),
        "PNL-01" => Pnl01Async(filter, ct), "PNL-02" => Pnl02Async(filter, ct), "PNL-03" => Pnl03Async(filter, ct),
        "PNL-04" => Pnl04Async(filter, ct), "PNL-05" => Pnl05Async(filter, ct), "PNL-06" => Pnl06Async(filter, ct),
        "PNL-07" => Pnl07Async(filter, ct), "PNL-08" => Pnl08Async(filter, ct), "PNL-09" => Pnl09Async(filter, ct),
        "PNL-10" => Pnl10Async(filter, ct), "PNL-11" => Pnl11Async(filter, ct), "PNL-12" => Pnl12Async(filter, ct),
        "PNL-13" => Pnl13Async(filter, ct),
        "INV-01" => Inv01Async(filter, ct), "INV-02" => Inv02Async(filter, ct), "INV-03" => Inv03Async(filter, ct),
        "INV-04" => Inv04Async(filter, ct), "INV-05" => Inv05Async(filter, ct), "INV-06" => Inv06Async(filter, ct),
        "INV-07" => Inv07Async(filter, ct), "INV-08" => Inv08Async(filter, ct), "INV-09" => Inv09Async(filter, ct),
        "INV-10" => Inv10Async(filter, ct), "INV-11" => Inv11Async(filter, ct), "INV-12" => Inv12Async(filter, ct),
        "INV-13" => Inv13Async(filter, ct), "INV-14" => Inv14Async(filter, ct), "INV-15" => Inv15Async(filter, ct),
        "INV-16" => Inv16Async(filter, ct), "INV-17" => Inv17Async(filter, ct), "INV-18" => Inv18Async(filter, ct),
        "MST-01" => Mst01Async(filter, ct), "MST-02" => Mst02Async(filter, ct), "MST-03" => Mst03Async(filter, ct),
        "MST-04" => Mst04Async(filter, ct), "MST-05" => Mst05Async(filter, ct),
        "AUD-01" => Aud01Async(filter, ct), "AUD-02" => Aud02Async(filter, ct), "AUD-03" => Aud03Async(filter, ct),
        _ => throw new NotFoundException($"Report {code} was not found.")
    };

    /// <summary>Bulk "who created it, when" for a report column the underlying table itself never stored
    /// (several of CC-42's own tables have no CreatedBy/CreatedOn) — one shared helper reused by CUS-01, TRP-07,
    /// TRP-10, TRP-11 and MST-01 rather than five near-identical audit queries.</summary>
    private async Task<Dictionary<string, (DateTime? On, string? By)>> AuditCreatedInfoAsync(string entity, IEnumerable<string> recordIds, CancellationToken ct)
    {
        var ids = recordIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<string, (DateTime?, string?)>();
        var entries = await db.Set<AuditEntry>().AsNoTracking()
            .Where(a => a.TenantId == tenant.TenantId && a.Entity == entity && a.Action == AuditActions.Created && ids.Contains(a.RecordId))
            .ToListAsync(ct);
        return entries.GroupBy(a => a.RecordId).ToDictionary(g => g.Key, g => (g.Min(a => (DateTime?)a.OccurredAt), g.First().UserName));
    }
}
