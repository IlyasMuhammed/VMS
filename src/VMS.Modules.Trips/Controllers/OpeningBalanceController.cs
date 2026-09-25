using Microsoft.AspNetCore.Mvc;
using VMS.Modules.Trips.Models;
using VMS.Modules.Trips.Services;
using VMS.Shared.Authorization;
using VMS.Shared.Pagination;

namespace VMS.Modules.Trips.Controllers;

/// <summary>§40A L16, §47.2: "POST /api/customers/{id}/opening-balance" — `Ledger.OpeningBalance` (Admin with
/// Finance approval). The bulk importer (TASKS.md's own CC-40 scope note) is a second, JSON-array route this
/// task adds beyond §47.2's own literal single-customer endpoint — see <see cref="OpeningBalanceImportRow"/>'s
/// own doc comment for why it is not a raw multipart CSV upload.</summary>
[ApiController]
public sealed class OpeningBalanceController(IOpeningBalanceService openingBalances) : ControllerBase
{
    [HttpPost("api/customers/{customerId:int}/opening-balance")]
    [RequirePermission(PermissionCodes.TRP_LEDGER_OPENINGBALANCE)]
    public async Task<IActionResult> Post(int customerId, [FromBody] PostOpeningBalanceRequest request) =>
        Ok(ApiResponse<OpeningBalanceModel>.Ok(await openingBalances.PostAsync(customerId, request, User.GetUserId()), "Opening balance posted."));

    [HttpPost("api/opening-balances/import")]
    [RequirePermission(PermissionCodes.TRP_LEDGER_OPENINGBALANCE)]
    public async Task<IActionResult> Import([FromBody] ImportOpeningBalancesRequest request) =>
        Ok(ApiResponse<OpeningBalanceImportResultModel>.Ok(await openingBalances.ImportAsync(request, User.GetUserId()), "Import complete."));
}
