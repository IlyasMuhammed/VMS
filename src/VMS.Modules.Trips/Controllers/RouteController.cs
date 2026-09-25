using Microsoft.AspNetCore.Mvc;
using VMS.Modules.Trips.Models;
using VMS.Modules.Trips.Services;
using VMS.Shared.Authorization;
using VMS.Shared.Pagination;

namespace VMS.Modules.Trips.Controllers;

/// <summary>Routes and route stops (FSD §17). Admin, Fleet Manager only (no separate view row — matches §44's
/// "City, route edit" row exactly; unlike City, routes are not "all view").</summary>
[ApiController]
[Route("api/routes")]
public sealed class RouteController(IRouteService routes) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCodes.TRP_ROUTE_EDIT)]
    public async Task<IActionResult> List([FromQuery] bool includeInactive = false) =>
        Ok(ApiResponse<IReadOnlyList<RouteModel>>.Ok(await routes.ListAsync(includeInactive)));

    [HttpGet("{routeId:int}")]
    [RequirePermission(PermissionCodes.TRP_ROUTE_EDIT)]
    public async Task<IActionResult> Get(int routeId) => Ok(ApiResponse<RouteModel>.Ok(await routes.GetAsync(routeId)));

    [HttpPost]
    [RequirePermission(PermissionCodes.TRP_ROUTE_EDIT)]
    public async Task<IActionResult> Create([FromBody] CreateRouteRequest request) =>
        Ok(ApiResponse<RouteModel>.Ok(await routes.CreateAsync(request)));

    [HttpPut("{routeId:int}")]
    [RequirePermission(PermissionCodes.TRP_ROUTE_EDIT)]
    public async Task<IActionResult> Update(int routeId, [FromBody] UpdateRouteRequest request) =>
        Ok(ApiResponse<RouteModel>.Ok(await routes.UpdateAsync(routeId, request)));

    [HttpPut("{routeId:int}/stops")]
    [RequirePermission(PermissionCodes.TRP_ROUTE_EDIT)]
    public async Task<IActionResult> UpdateStops(int routeId, [FromBody] UpdateRouteStopsRequest request) =>
        Ok(ApiResponse<RouteModel>.Ok(await routes.UpdateStopsAsync(routeId, request)));
}
