using Microsoft.AspNetCore.Mvc;
using VMS.Modules.Trips.Models;
using VMS.Modules.Trips.Services;
using VMS.Shared.Authorization;
using VMS.Shared.Pagination;

namespace VMS.Modules.Trips.Controllers;

/// <summary>§40A.5 LR-7, §47.2: "POST /api/ledger-periods/{yyyymm}/close · /reopen" — `Ledger.PeriodLock`
/// (Finance Lead, Admin).</summary>
[ApiController]
[Route("api/ledger-periods")]
public sealed class LedgerPeriodController(ILedgerPeriodService periods) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCodes.TRP_LEDGER_VIEW)]
    public async Task<IActionResult> List() =>
        Ok(ApiResponse<IReadOnlyList<LedgerPeriodModel>>.Ok(await periods.ListAsync()));

    [HttpPost("{yearMonth}/close")]
    [RequirePermission(PermissionCodes.TRP_LEDGER_PERIODLOCK)]
    public async Task<IActionResult> Close(string yearMonth, [FromBody] LedgerPeriodRequest request) =>
        Ok(ApiResponse<LedgerPeriodModel>.Ok(await periods.CloseAsync(yearMonth, request, User.GetUserId()), $"{yearMonth} closed."));

    [HttpPost("{yearMonth}/reopen")]
    [RequirePermission(PermissionCodes.TRP_LEDGER_PERIODLOCK)]
    public async Task<IActionResult> Reopen(string yearMonth, [FromBody] LedgerPeriodRequest request) =>
        Ok(ApiResponse<LedgerPeriodModel>.Ok(await periods.ReopenAsync(yearMonth, request, User.GetUserId()), $"{yearMonth} reopened."));
}
