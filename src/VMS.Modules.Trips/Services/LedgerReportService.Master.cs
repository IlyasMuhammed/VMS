using Microsoft.EntityFrameworkCore;
using VMS.Modules.Trips.Domain;
using VMS.Modules.Trips.Models;

namespace VMS.Modules.Trips.Services;

/// <summary>§42.6: MST-01..05.</summary>
internal sealed partial class LedgerReportService
{
    private async Task<(List<string>, List<Dictionary<string, object?>>)> Mst01Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "customerCode", "tripConfiguration", "effectiveFrom", "effectiveTo", "rate", "status", "changedBy", "changedOn" };
        var rates = await db.TripRates.AsNoTracking().Where(r => r.TenantId == tenant.TenantId
            && (f.CustomerId == null || r.CustomerId == f.CustomerId) && (f.TripConfigurationId == null || r.TripConfigurationId == f.TripConfigurationId)).ToListAsync(ct);
        if (rates.Count == 0) return (columns, []);

        var codes = await CustomerCodesAsync(rates.Select(r => r.CustomerId), ct);
        var configIds = rates.Select(r => r.TripConfigurationId).Distinct().ToList();
        var configCodes = await db.TripConfigurations.AsNoTracking().Where(c => c.TenantId == tenant.TenantId && configIds.Contains(c.TripConfigurationId)).ToDictionaryAsync(c => c.TripConfigurationId, c => c.TripCode, ct);
        var created = await AuditCreatedInfoAsync("TripConfiguration", rates.Select(r => r.TripRateId.ToString()), ct);

        var rows = rates.Select(r => new Dictionary<string, object?>
        {
            ["customerCode"] = codes.GetValueOrDefault(r.CustomerId), ["tripConfiguration"] = configCodes.GetValueOrDefault(r.TripConfigurationId),
            ["effectiveFrom"] = r.EffectiveFrom, ["effectiveTo"] = r.EffectiveTo, ["rate"] = r.RateAmount, ["status"] = r.Status,
            ["changedBy"] = created.GetValueOrDefault(r.TripRateId.ToString()).By, ["changedOn"] = created.GetValueOrDefault(r.TripRateId.ToString()).On
        }).ToList();
        return (columns, rows);
    }

    // MST-02: a simpler, honestly-approximate reading of "dates with no rate" — every Active configuration whose
    // rates don't currently cover today, rather than a full scan for every historical gap.
    private async Task<(List<string>, List<Dictionary<string, object?>>)> Mst02Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "tripConfiguration", "missingFrom" };
        var today = await clock.TodayAsync(tenant.TenantId);
        var configs = await db.TripConfigurations.AsNoTracking().Where(c => c.TenantId == tenant.TenantId && c.Status == TripConfigurationStatuses.Active
            && (f.CustomerId == null || c.CustomerId == f.CustomerId)).ToListAsync(ct);
        if (configs.Count == 0) return (columns, []);
        var configIds = configs.Select(c => c.TripConfigurationId).ToList();
        var coveredIds = await db.TripRates.AsNoTracking().Where(r => r.TenantId == tenant.TenantId && configIds.Contains(r.TripConfigurationId) && r.Status == ActiveInactiveStatuses.Active
            && r.EffectiveFrom <= today && (r.EffectiveTo == null || r.EffectiveTo >= today)).Select(r => r.TripConfigurationId).Distinct().ToListAsync(ct);

        var rows = configs.Where(c => !coveredIds.Contains(c.TripConfigurationId)).Select(c => new Dictionary<string, object?> { ["tripConfiguration"] = c.TripCode, ["missingFrom"] = today }).ToList();
        return (columns, rows);
    }

    private async Task<(List<string>, List<Dictionary<string, object?>>)> Mst03Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "routeCode", "routeName", "originCity", "destinationCity", "stopCount", "distanceKm" };
        var routes = await db.Routes.AsNoTracking().Where(r => r.TenantId == tenant.TenantId).ToListAsync(ct);
        if (routes.Count == 0) return (columns, []);

        var cityIds = routes.SelectMany(r => new[] { r.OriginCityId, r.DestinationCityId }).Distinct().ToList();
        var cities = await db.Cities.AsNoTracking().Where(c => c.TenantId == tenant.TenantId && cityIds.Contains(c.CityId)).ToDictionaryAsync(c => c.CityId, c => c.Abbreviation, ct);
        var routeIds = routes.Select(r => r.RouteId).ToList();
        var stopCounts = await db.RouteStops.AsNoTracking().Where(s => s.TenantId == tenant.TenantId && routeIds.Contains(s.RouteId))
            .GroupBy(s => s.RouteId).Select(g => new { RouteId = g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.RouteId, g => g.Count, ct);

        var rows = routes.Select(r => new Dictionary<string, object?>
        {
            ["routeCode"] = r.RouteCode, ["routeName"] = r.RouteName, ["originCity"] = cities.GetValueOrDefault(r.OriginCityId), ["destinationCity"] = cities.GetValueOrDefault(r.DestinationCityId),
            ["stopCount"] = stopCounts.GetValueOrDefault(r.RouteId, 0), ["distanceKm"] = r.DistanceKm
        }).ToList();
        return (columns, rows);
    }

    private async Task<(List<string>, List<Dictionary<string, object?>>)> Mst04Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "tripConfiguration", "vehicleRegNo", "effectiveFrom", "effectiveTo" };
        var assignments = await db.TripConfigurationVehicles.AsNoTracking().Where(v => v.TenantId == tenant.TenantId
            && (f.TripConfigurationId == null || v.TripConfigurationId == f.TripConfigurationId) && (f.VehicleId == null || v.VehicleId == f.VehicleId)).ToListAsync(ct);
        if (assignments.Count == 0) return (columns, []);

        var configIds = assignments.Select(a => a.TripConfigurationId).Distinct().ToList();
        var configCodes = await db.TripConfigurations.AsNoTracking().Where(c => c.TenantId == tenant.TenantId && configIds.Contains(c.TripConfigurationId)).ToDictionaryAsync(c => c.TripConfigurationId, c => c.TripCode, ct);
        var vehicleInfos = await vehicles.FindManyAsync(assignments.Select(a => a.VehicleId).Distinct(), ct);

        var rows = assignments.Select(a => new Dictionary<string, object?>
        {
            ["tripConfiguration"] = configCodes.GetValueOrDefault(a.TripConfigurationId), ["vehicleRegNo"] = vehicleInfos.GetValueOrDefault(a.VehicleId)?.RegistrationNo,
            ["effectiveFrom"] = a.EffectiveFrom, ["effectiveTo"] = a.EffectiveTo
        }).ToList();
        return (columns, rows);
    }

    private async Task<(List<string>, List<Dictionary<string, object?>>)> Mst05Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "customerCode", "templateName", "version", "templateType", "effectiveFrom", "isDefault", "status" };
        var templates = await db.CustomerInvoiceTemplates.AsNoTracking().Where(t => t.TenantId == tenant.TenantId
            && (f.CustomerId == null || t.CustomerId == f.CustomerId)).ToListAsync(ct);
        if (templates.Count == 0) return (columns, []);
        var codes = await CustomerCodesAsync(templates.Select(t => t.CustomerId), ct);

        var rows = templates.Select(t => new Dictionary<string, object?>
        {
            ["customerCode"] = codes.GetValueOrDefault(t.CustomerId), ["templateName"] = t.TemplateName, ["version"] = t.Version, ["templateType"] = t.TemplateType,
            ["effectiveFrom"] = t.EffectiveFrom, ["isDefault"] = t.IsDefault, ["status"] = t.Status
        }).ToList();
        return (columns, rows);
    }
}
