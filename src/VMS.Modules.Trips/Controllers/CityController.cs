using Microsoft.AspNetCore.Mvc;
using VMS.Modules.Trips.Models;
using VMS.Modules.Trips.Services;
using VMS.Shared.Authorization;
using VMS.Shared.Pagination;

namespace VMS.Modules.Trips.Controllers;

/// <summary>The city master (FSD §16). "Admin, Fleet Manager edit; all view" — view needs only a valid sign-in,
/// the same as the platform's own lookups.</summary>
[ApiController]
[Route("api/cities")]
[AuthenticatedOnly]
public sealed class CityController(ICityService cities) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] bool includeInactive = false) =>
        Ok(ApiResponse<IReadOnlyList<CityModel>>.Ok(await cities.ListAsync(includeInactive)));

    [HttpPost]
    [RequirePermission(PermissionCodes.TRP_CITY_EDIT)]
    public async Task<IActionResult> Create([FromBody] SaveCityRequest request) =>
        Ok(ApiResponse<CityModel>.Ok(await cities.CreateAsync(request)));

    [HttpPut("{cityId:int}")]
    [RequirePermission(PermissionCodes.TRP_CITY_EDIT)]
    public async Task<IActionResult> Update(int cityId, [FromBody] SaveCityRequest request) =>
        Ok(ApiResponse<CityModel>.Ok(await cities.UpdateAsync(cityId, request)));
}
