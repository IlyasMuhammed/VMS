using Microsoft.AspNetCore.Mvc;
using VMS.Modules.Trips.Models;
using VMS.Modules.Trips.Services;
using VMS.Shared.Authorization;
using VMS.Shared.Pagination;

namespace VMS.Modules.Trips.Controllers;

/// <summary>§40A.5 LR-4: the nightly reconciliation job's own latest result, read by Finance/Admin.</summary>
[ApiController]
[Route("api/ledger-reconciliation")]
public sealed class LedgerReconciliationController(ILedgerReconciliationJob job) : ControllerBase
{
    [HttpGet("latest")]
    [RequirePermission(PermissionCodes.TRP_LEDGER_VIEW)]
    public async Task<IActionResult> Latest() =>
        Ok(ApiResponse<LedgerReconciliationRunModel?>.Ok(await job.GetLatestAsync()));
}
