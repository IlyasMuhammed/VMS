using Microsoft.AspNetCore.Mvc;
using VMS.Modules.Vehicles.Services;
using VMS.Shared.Authorization;
using VMS.Shared.Pagination;

namespace VMS.Modules.Vehicles.Controllers;

/// <summary>
/// A manual trigger for the nightly recurring-charge job (FSD §19A.4, BR-VH-030): the hosted service calls the same
/// <see cref="IRecurringChargeGenerator"/> unattended, on an interval; this is for support to re-run it on demand, and it is
/// how the job's idempotency is checked from outside a unit test — call it twice, get the same entries once.
/// </summary>
[ApiController]
[Route("api/admin/jobs/recurring-charges")]
public class RecurringChargeJobController(IRecurringChargeGenerator generator) : ControllerBase
{
    [HttpPost("run")]
    [RequirePermission(PermissionCodes.ADM_CONFIG_MANAGE)]
    public async Task<IActionResult> Run() => Ok(ApiResponse<RecurringChargeGenerationResult>.Ok(await generator.RunAsync()));
}
