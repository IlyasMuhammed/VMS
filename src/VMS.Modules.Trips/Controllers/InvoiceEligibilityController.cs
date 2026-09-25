using Microsoft.AspNetCore.Mvc;
using VMS.Modules.Trips.Models;
using VMS.Modules.Trips.Services;
using VMS.Shared.Authorization;
using VMS.Shared.Pagination;

namespace VMS.Modules.Trips.Controllers;

/// <summary>Eligible-trip search and overlap check (§32.1, §39, §47.3). Read-only; invoice creation itself (a
/// later CC-25 task) revalidates everything again inside its own transaction.</summary>
[ApiController]
[Route("api/invoices")]
public sealed class InvoiceEligibilityController(IInvoiceEligibilityService eligibility) : ControllerBase
{
    [HttpPost("search-eligible-trips")]
    [RequirePermission(PermissionCodes.TRP_INVOICE_GENERATE)]
    public async Task<IActionResult> Search([FromBody] EligibleTripsQuery query) => Ok(ApiResponse<EligibleTripsResult>.Ok(await eligibility.SearchAsync(query)));
}
