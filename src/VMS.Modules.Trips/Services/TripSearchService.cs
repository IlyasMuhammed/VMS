using Microsoft.EntityFrameworkCore;
using VMS.Modules.Trips.Data;
using VMS.Modules.Trips.Domain;
using VMS.Modules.Trips.Models;
using VMS.Shared.Common;
using VMS.Shared.Pagination;
using VMS.Shared.Partners;
using VMS.Shared.Vehicles;

namespace VMS.Modules.Trips.Services;

/// <summary>The Trip List / Trip Desk board (FSD §48.4's unnumbered "Trip List (desk)" row) — the one screen this
/// module never had a read path for before now: every earlier trip-reading task (CC-12..22) only ever fetches
/// one trip by id, assuming a caller that already has it. Built as part of CC-43 (back-office UI) once the
/// screen it feeds made the gap impossible to defer any further — a Trip Details screen with nowhere to
/// navigate from, and "Re-price Trips"/"Resolve Missing Rates" with no way to show what they touched, are not a
/// usable UI. Page first, then resolve names in a handful of batched lookups keyed by the ids on that one page —
/// the same shape <see cref="TripConfigurationService.ListVehiclesAsync"/> and CC-42's own <c>AuditCreatedInfoAsync</c>
/// already established, not a join per row.</summary>
public interface ITripSearchService
{
    Task<PaginatedResponse<TripListItem>> SearchAsync(TripSearchFilter filter, int page, int pageSize, CancellationToken ct = default);
}

internal sealed class TripSearchService(TripsDbContext db, ITenantContext tenant, IVehicleDirectory vehicles, IPartnerDirectory partners) : ITripSearchService
{
    public async Task<PaginatedResponse<TripListItem>> SearchAsync(TripSearchFilter filter, int page, int pageSize, CancellationToken ct = default)
    {
        var query = db.Trips.AsNoTracking().Where(t => t.TenantId == tenant.TenantId);
        if (filter.FromDate is { } from) query = query.Where(t => t.TripDate >= from);
        if (filter.ToDate is { } to) query = query.Where(t => t.TripDate <= to);
        if (filter.CustomerId is { } customerId) query = query.Where(t => t.CustomerId == customerId);
        if (filter.VehicleId is { } vehicleId) query = query.Where(t => t.VehicleId == vehicleId);
        if (filter.DriverId is { } driverId) query = query.Where(t => t.DriverId == driverId);
        if (!string.IsNullOrWhiteSpace(filter.Status)) query = query.Where(t => t.Status == filter.Status);
        if (!string.IsNullOrWhiteSpace(filter.TripType)) query = query.Where(t => t.TripType == filter.TripType);
        if (filter.IsActive is { } isActive) query = query.Where(t => t.IsActive == isActive);
        if (filter.Invoiced is { } invoiced) query = query.Where(t => (t.InvoiceId != null) == invoiced);

        var total = await query.CountAsync(ct);
        page = page <= 0 ? 1 : page;
        pageSize = pageSize <= 0 ? 25 : Math.Min(pageSize, 200);
        var rows = await query.OrderByDescending(t => t.TripDate).ThenByDescending(t => t.TripId)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        var items = await ToItemsAsync(rows, ct);
        return new PaginatedResponse<TripListItem> { Items = items, TotalCount = total, Page = page, PageSize = pageSize };
    }

    private async Task<List<TripListItem>> ToItemsAsync(List<Trip> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return [];

        var customerIds = rows.Select(t => t.CustomerId).Distinct().ToList();
        var customers = await db.Customers.AsNoTracking().Where(c => c.TenantId == tenant.TenantId && customerIds.Contains(c.CustomerId))
            .ToDictionaryAsync(c => c.CustomerId, c => c.CustomerName, ct);

        var vehicleIds = rows.Select(t => t.VehicleId).Distinct();
        var vehicleInfo = await vehicles.FindManyAsync(vehicleIds, ct);

        var driverIds = rows.Where(t => t.DriverId is not null).Select(t => t.DriverId!.Value).Distinct();
        var driverInfo = await partners.FindManyAsync(driverIds, ct);

        var routeIds = rows.Where(t => t.RouteId is not null).Select(t => t.RouteId!.Value).Distinct().ToList();
        var routes = routeIds.Count == 0 ? new Dictionary<int, Route>() : await db.Routes.AsNoTracking().Where(r => r.TenantId == tenant.TenantId && routeIds.Contains(r.RouteId)).ToDictionaryAsync(r => r.RouteId, ct);

        var cityIds = rows.SelectMany(t => new[] { t.FromCityId, t.ToCityId }).Where(id => id is not null).Select(id => id!.Value).Distinct().ToList();
        var cities = cityIds.Count == 0 ? new Dictionary<int, City>() : await db.Cities.AsNoTracking().Where(c => c.TenantId == tenant.TenantId && cityIds.Contains(c.CityId)).ToDictionaryAsync(c => c.CityId, ct);

        var invoiceIds = rows.Where(t => t.InvoiceId is not null).Select(t => t.InvoiceId!.Value).Distinct().ToList();
        var invoices = invoiceIds.Count == 0 ? new Dictionary<long, string>() : await db.Invoices.AsNoTracking().Where(i => i.TenantId == tenant.TenantId && invoiceIds.Contains(i.InvoiceId)).ToDictionaryAsync(i => i.InvoiceId, i => i.InvoiceNumber, ct);

        var tripIds = rows.Select(t => t.TripId).ToList();
        var latestPods = await db.TripPODs.AsNoTracking().Where(p => p.TenantId == tenant.TenantId && tripIds.Contains(p.TripId))
            .GroupBy(p => p.TripId).Select(g => g.OrderByDescending(p => p.UploadedAtUtc).First()).ToDictionaryAsync(p => p.TripId, p => p.Status, ct);

        return rows.Select(t => new TripListItem
        {
            TripId = t.TripId, TripNumber = t.TripNumber, TripType = t.TripType, TripDate = t.TripDate,
            CustomerId = t.CustomerId, CustomerName = customers.GetValueOrDefault(t.CustomerId, $"#{t.CustomerId}"), CustomerTripReference = t.CustomerTripReference,
            RouteLabel = RouteLabelOf(t, routes, cities), VehicleId = t.VehicleId, VehicleRegistrationNo = vehicleInfo.GetValueOrDefault(t.VehicleId)?.RegistrationNo,
            DriverId = t.DriverId, DriverName = t.DriverId is { } d ? driverInfo.GetValueOrDefault(d)?.DisplayName : null,
            Status = t.Status, TripAmount = t.TripAmount, CurrencyCode = t.CurrencyCode, RateMissing = t.RateMissing,
            PodStatus = latestPods.GetValueOrDefault(t.TripId), InvoiceId = t.InvoiceId, InvoiceNumber = t.InvoiceId is { } inv ? invoices.GetValueOrDefault(inv) : null,
            IsActive = t.IsActive,
        }).ToList();
    }

    private static string? RouteLabelOf(Trip t, IReadOnlyDictionary<int, Route> routes, IReadOnlyDictionary<int, City> cities)
    {
        if (t.TripType == TripTypes.Fixed) return t.RouteId is { } rid && routes.TryGetValue(rid, out var route) ? route.RouteCode : null;

        var from = t.FromLocationType == TripLocationTypes.City ? (t.FromCityId is { } fid && cities.TryGetValue(fid, out var fc) ? fc.Abbreviation : "?") : t.FromOtherLocationName ?? "?";
        var to = t.ToLocationType == TripLocationTypes.City ? (t.ToCityId is { } tid && cities.TryGetValue(tid, out var tc) ? tc.Abbreviation : "?") : t.ToOtherLocationName ?? "?";
        return t.FromLocationType is null ? null : $"{from} → {to}";
    }
}
