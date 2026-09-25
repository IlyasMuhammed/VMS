using Microsoft.AspNetCore.Mvc;
using VMS.Modules.Trips.Models;
using VMS.Modules.Trips.Services;
using VMS.Shared.Authorization;
using VMS.Shared.Pagination;

namespace VMS.Modules.Trips.Controllers;

/// <summary>§42.2, §47.2: "GET /api/reports/{code}?filters · POST /api/reports/{code}/export" — one generic
/// `TRP.REPORT.VIEW` permission for every report code (CC-00's own catalogue design decision, not one
/// permission per report), matching the existing `FIN.REPORT.VIEW`/`DOC.REGISTER.VIEW` pattern.</summary>
[ApiController]
[Route("api/reports/{code}")]
public sealed class LedgerReportController(ILedgerReportService reports) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCodes.TRP_REPORT_VIEW)]
    public async Task<IActionResult> Run(string code, [FromQuery] ReportFilter filter) =>
        Ok(ApiResponse<ReportResultModel>.Ok(await reports.RunAsync(code, filter, RunByName())));

    [HttpPost("export")]
    [RequirePermission(PermissionCodes.TRP_REPORT_VIEW)]
    public async Task<IActionResult> Export(string code, [FromQuery] ReportFilter filter)
    {
        var result = await reports.ExportOrQueueAsync(code, filter, RunByName(), User.GetUserId());
        if (result.Ready) return File(result.Bytes!, "text/csv", result.FileName);
        // §42.9: "run as background jobs and notify the user when the file is ready" — the notification itself is
        // the caller polling GET /api/report-exports/{id}; no separate push channel for this one flow.
        return Ok(ApiResponse<ReportExportResultModel>.Ok(result, $"Report is large; queued as job {result.JobId}."));
    }

    private string RunByName() => User.FindFirst("user_name")?.Value ?? "Unknown";
}

[ApiController]
[Route("api/report-exports")]
public sealed class ReportExportController(ILedgerReportService reports) : ControllerBase
{
    [HttpGet("{jobId:long}")]
    [RequirePermission(PermissionCodes.TRP_REPORT_VIEW)]
    public async Task<IActionResult> Get(long jobId) =>
        Ok(ApiResponse<ReportExportResultModel>.Ok(await reports.GetExportJobAsync(jobId)));
}
