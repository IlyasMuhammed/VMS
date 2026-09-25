using Microsoft.AspNetCore.Mvc;
using VMS.Modules.Trips.Models;
using VMS.Modules.Trips.Services;
using VMS.Shared.Authorization;
using VMS.Shared.Pagination;

namespace VMS.Modules.Trips.Controllers;

/// <summary>Rate re-resolution (§26, CC-17): "Resolve missing rates" and "Re-price trips" — the two of §26's three
/// re-resolution triggers that have their own explicit action (the third, automatic re-resolution on a Trip Edit,
/// has no edit endpoint to hang off yet).</summary>
[ApiController]
[Route("api/trips")]
public sealed class TripRepricingController(ITripRepricingService repricing) : ControllerBase
{
    [HttpPost("resolve-missing-rates")]
    [RequirePermission(PermissionCodes.TRP_RATE_CONFIGURE)]
    public async Task<IActionResult> ResolveMissingRates([FromBody] ResolveMissingRatesRequest request) =>
        Ok(ApiResponse<ResolveMissingRatesResult>.Ok(await repricing.ResolveMissingRatesAsync(request, User.GetUserId())));

    [HttpPost("reprice")]
    [RequirePermission(PermissionCodes.TRP_RATE_REPRICE)]
    public async Task<IActionResult> Reprice([FromBody] RepriceTripsRequest request) =>
        Ok(ApiResponse<RepriceResult>.Ok(await repricing.RepriceAsync(request, User.GetUserId())));
}
