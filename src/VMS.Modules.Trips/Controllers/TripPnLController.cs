using Microsoft.AspNetCore.Mvc;
using VMS.Modules.Trips.Models;
using VMS.Modules.Trips.Services;
using VMS.Shared.Authorization;
using VMS.Shared.Pagination;

namespace VMS.Modules.Trips.Controllers;

/// <summary>Trip operational P&amp;L (§31, AC-22).</summary>
[ApiController]
[Route("api/trips")]
public sealed class TripPnLController(ITripPnLService pnl) : ControllerBase
{
    [HttpGet("{tripId:long}/pnl")]
    [RequirePermission(PermissionCodes.TRP_PNL_VIEW)]
    public async Task<IActionResult> Get(long tripId) => Ok(ApiResponse<TripPnLModel>.Ok(await pnl.GetAsync(tripId)));

    /// <summary>Not a literal endpoint in §47.2's own list — added so "unpriced trips excluded from totals with a
    /// count shown" (§31) is a real, tested calculation rather than a single-trip endpoint that can't express a
    /// count at all. The full by-vehicle/driver/customer/route/month report screens (PNL-08..13) are a separate,
    /// later Reports task; this is the underlying per-trip calculation they will read.</summary>
    [HttpGet("pnl-summary")]
    [RequirePermission(PermissionCodes.TRP_PNL_VIEW)]
    public async Task<IActionResult> Summary([FromQuery] TripPnLQuery query) => Ok(ApiResponse<TripPnLSummaryModel>.Ok(await pnl.SummaryAsync(query)));
}
