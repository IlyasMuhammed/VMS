using Microsoft.EntityFrameworkCore;
using VMS.Modules.Trips.Domain;
using VMS.Modules.Trips.Models;

namespace VMS.Modules.Trips.Services;

/// <summary>§42.3: TRP-01..16.</summary>
internal sealed partial class LedgerReportService
{
    /// <summary>§42's own "Common filters": period (TripDate), customer, vehicle, driver, trip type, trip status,
    /// active — shared by every trip-register-shaped report in this file.</summary>
    private IQueryable<Trip> FilteredTrips(ReportFilter f) => db.Trips.AsNoTracking().Where(t => t.TenantId == tenant.TenantId
        && (f.From == null || t.TripDate >= f.From) && (f.To == null || t.TripDate <= f.To) && (f.CustomerId == null || t.CustomerId == f.CustomerId)
        && (f.VehicleId == null || t.VehicleId == f.VehicleId) && (f.DriverId == null || t.DriverId == f.DriverId)
        && (string.IsNullOrEmpty(f.TripType) || t.TripType == f.TripType) && (string.IsNullOrEmpty(f.Status) || t.Status == f.Status)
        && (f.Active == null || t.IsActive == f.Active));

    private async Task<(List<string>, List<Dictionary<string, object?>>)> Trp01Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "tripNumber", "tripDate", "tripType", "customerCode", "customerTripReference", "configOrRoute",
            "vehicleRegNo", "driverName", "status", "isActive", "tripAmount", "invoiceNumber" };
        var trips = await FilteredTrips(f).ToListAsync(ct);
        if (trips.Count == 0) return (columns, []);
        return (columns, await ToTripRowsAsync(trips, ct));
    }

    private async Task<(List<string>, List<Dictionary<string, object?>>)> Trp02Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "customerCode", "tripCount", "tripAmount" };
        var trips = await FilteredTrips(f).ToListAsync(ct);
        if (trips.Count == 0) return (columns, []);
        var codes = await CustomerCodesAsync(trips.Select(t => t.CustomerId), ct);
        var rows = trips.GroupBy(t => t.CustomerId).Select(g => new Dictionary<string, object?>
        { ["customerCode"] = codes.GetValueOrDefault(g.Key), ["tripCount"] = g.Count(), ["tripAmount"] = g.Sum(t => t.TripAmount ?? 0) }).ToList();
        return (columns, rows);
    }

    private async Task<(List<string>, List<Dictionary<string, object?>>)> Trp03Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "vehicleRegNo", "tripCount", "totalKm", "tripAmount" };
        var trips = await FilteredTrips(f).ToListAsync(ct);
        if (trips.Count == 0) return (columns, []);
        var vehicleInfos = await vehicles.FindManyAsync(trips.Select(t => t.VehicleId).Distinct(), ct);
        var rows = trips.GroupBy(t => t.VehicleId).Select(g => new Dictionary<string, object?>
        {
            ["vehicleRegNo"] = vehicleInfos.GetValueOrDefault(g.Key)?.RegistrationNo, ["tripCount"] = g.Count(),
            ["totalKm"] = g.Sum(t => t.EndOdometer is { } end && t.StartOdometer is { } start ? end - start : 0), ["tripAmount"] = g.Sum(t => t.TripAmount ?? 0)
        }).ToList();
        return (columns, rows);
    }

    private async Task<(List<string>, List<Dictionary<string, object?>>)> Trp04Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "driverName", "tripCount", "totalKm", "overrideCount" };
        var trips = await FilteredTrips(f).Where(t => t.DriverId != null).ToListAsync(ct);
        if (trips.Count == 0) return (columns, []);
        var driverInfos = await partners.FindManyAsync(trips.Select(t => t.DriverId!.Value).Distinct(), ct);
        var rows = trips.GroupBy(t => t.DriverId!.Value).Select(g => new Dictionary<string, object?>
        {
            ["driverName"] = driverInfos.GetValueOrDefault(g.Key)?.DisplayName, ["tripCount"] = g.Count(),
            ["totalKm"] = g.Sum(t => t.EndOdometer is { } end && t.StartOdometer is { } start ? end - start : 0),
            ["overrideCount"] = g.Count(t => t.IsDriverOverridden)
        }).ToList();
        return (columns, rows);
    }

    private async Task<(List<string>, List<Dictionary<string, object?>>)> Trp05Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "routeOrConfig", "tripCount", "tripAmount", "avgDurationHours" };
        var trips = await FilteredTrips(f).ToListAsync(ct);
        if (trips.Count == 0) return (columns, []);
        var configIds = trips.Where(t => t.TripConfigurationId != null).Select(t => t.TripConfigurationId!.Value).Distinct().ToList();
        var configCodes = configIds.Count == 0 ? new Dictionary<long, string>() : await db.TripConfigurations.AsNoTracking()
            .Where(c => c.TenantId == tenant.TenantId && configIds.Contains(c.TripConfigurationId)).ToDictionaryAsync(c => c.TripConfigurationId, c => c.TripCode, ct);

        var rows = trips.GroupBy(t => t.TripConfigurationId?.ToString() ?? $"Open-{t.RouteId}").Select(g =>
        {
            var durations = g.Where(t => t.ActualStart != null && t.ActualEnd != null).Select(t => (t.ActualEnd!.Value - t.ActualStart!.Value).TotalHours).ToList();
            var first = g.First();
            return new Dictionary<string, object?>
            {
                ["routeOrConfig"] = first.TripConfigurationId is { } id ? configCodes.GetValueOrDefault(id) : "Open trips",
                ["tripCount"] = g.Count(), ["tripAmount"] = g.Sum(t => t.TripAmount ?? 0), ["avgDurationHours"] = durations.Count == 0 ? null : (decimal?)Math.Round((decimal)durations.Average(), 1)
            };
        }).ToList();
        return (columns, rows);
    }

    private async Task<(List<string>, List<Dictionary<string, object?>>)> Trp06Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "tripNumber", "tripConfiguration", "tripRateId", "rateAmount", "rateEffectiveFrom", "rateEffectiveTo", "tripAmount" };
        var trips = await FilteredTrips(f).Where(t => t.TripType == TripTypes.Fixed).ToListAsync(ct);
        if (trips.Count == 0) return (columns, []);
        var configIds = trips.Where(t => t.TripConfigurationId != null).Select(t => t.TripConfigurationId!.Value).Distinct().ToList();
        var configCodes = configIds.Count == 0 ? new Dictionary<long, string>() : await db.TripConfigurations.AsNoTracking()
            .Where(c => c.TenantId == tenant.TenantId && configIds.Contains(c.TripConfigurationId)).ToDictionaryAsync(c => c.TripConfigurationId, c => c.TripCode, ct);

        var rows = trips.Select(t => new Dictionary<string, object?>
        {
            ["tripNumber"] = t.TripNumber, ["tripConfiguration"] = t.TripConfigurationId is { } id ? configCodes.GetValueOrDefault(id) : null,
            ["tripRateId"] = t.TripRateId, ["rateAmount"] = t.TripRateAmount, ["rateEffectiveFrom"] = t.RateEffectiveFrom, ["rateEffectiveTo"] = t.RateEffectiveTo,
            ["tripAmount"] = t.TripAmount
        }).ToList();
        return (columns, rows);
    }

    private async Task<(List<string>, List<Dictionary<string, object?>>)> Trp07Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "tripNumber", "fromLabel", "toLabel", "stopCount", "tripAmount", "enteredBy" };
        var trips = await FilteredTrips(f).Where(t => t.TripType == TripTypes.Open).ToListAsync(ct);
        if (trips.Count == 0) return (columns, []);
        var stopCounts = await db.TripStops.AsNoTracking().Where(s => s.TenantId == tenant.TenantId && trips.Select(t => t.TripId).Contains(s.TripId))
            .GroupBy(s => s.TripId).Select(g => new { TripId = g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.TripId, g => g.Count, ct);
        var created = await AuditCreatedInfoAsync("Trip", trips.Select(t => t.TripId.ToString()), ct);

        var rows = trips.Select(t => new Dictionary<string, object?>
        {
            ["tripNumber"] = t.TripNumber, ["fromLabel"] = t.FromOtherLocationName ?? "(city)", ["toLabel"] = t.ToOtherLocationName ?? "(city)",
            ["stopCount"] = stopCounts.GetValueOrDefault(t.TripId, 0), ["tripAmount"] = t.TripAmount, ["enteredBy"] = created.GetValueOrDefault(t.TripId.ToString()).By
        }).ToList();
        return (columns, rows);
    }

    private async Task<(List<string>, List<Dictionary<string, object?>>)> Trp08Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "tripNumber", "status", "sinceDateTime", "holdReason" };
        var trips = await FilteredTrips(f).ToListAsync(ct);
        if (trips.Count == 0) return (columns, []);
        // "Since" approximates the FSD's own field with the trip's own most recent timeline event, since Trip
        // itself has no dedicated "status changed on" column — the timeline (TripEvent) is the closest real record.
        var lastEvents = await db.TripEvents.AsNoTracking().Where(e => e.TenantId == tenant.TenantId && trips.Select(t => t.TripId).Contains(e.TripId))
            .GroupBy(e => e.TripId).Select(g => new { TripId = g.Key, Last = g.Max(e => e.EventDateTime) }).ToDictionaryAsync(g => g.TripId, g => g.Last, ct);

        var rows = trips.Select(t => new Dictionary<string, object?>
        {
            ["tripNumber"] = t.TripNumber, ["status"] = t.Status, ["sinceDateTime"] = lastEvents.GetValueOrDefault(t.TripId), ["holdReason"] = t.HoldReason
        }).ToList();
        return (columns, rows);
    }

    private async Task<(List<string>, List<Dictionary<string, object?>>)> Trp09Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "tripNumber", "eventType", "eventDateTime", "location", "odometer", "userName", "source" };
        var events = await db.TripEvents.AsNoTracking().Where(e => e.TenantId == tenant.TenantId && (f.InvoiceId == null)
            && (f.From == null || e.EventDateTime >= f.From.Value.ToDateTime(TimeOnly.MinValue)) && (f.To == null || e.EventDateTime <= f.To.Value.ToDateTime(TimeOnly.MaxValue))
            && (string.IsNullOrEmpty(f.Method) || e.Source == f.Method)).ToListAsync(ct);
        if (events.Count == 0) return (columns, []);

        var tripIds = events.Select(e => e.TripId).Distinct().ToList();
        var tripNumbers = await db.Trips.AsNoTracking().Where(t => t.TenantId == tenant.TenantId && tripIds.Contains(t.TripId)).ToDictionaryAsync(t => t.TripId, t => t.TripNumber, ct);
        var userIds = events.Select(e => e.UserId).Distinct().ToList();
        var userInfos = await partners.FindManyAsync(userIds, ct);

        var rows = events.OrderBy(e => e.EventDateTime).Select(e => new Dictionary<string, object?>
        {
            ["tripNumber"] = tripNumbers.GetValueOrDefault(e.TripId), ["eventType"] = e.EventType, ["eventDateTime"] = e.EventDateTime,
            ["location"] = e.LocationText, ["odometer"] = e.Odometer, ["userName"] = userInfos.GetValueOrDefault(e.UserId)?.DisplayName, ["source"] = e.Source
        }).ToList();
        return (columns, rows);
    }

    private async Task<(List<string>, List<Dictionary<string, object?>>)> Trp10Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "tripNumber", "status", "reason", "by", "since" };
        var trips = await FilteredTrips(f).Where(t => t.Status == TripStatuses.OnHold || t.Status == TripStatuses.Cancelled).ToListAsync(ct);
        if (trips.Count == 0) return (columns, []);
        var lastEvents = await db.TripEvents.AsNoTracking().Where(e => e.TenantId == tenant.TenantId && trips.Select(t => t.TripId).Contains(e.TripId)
                && (e.EventType == TripEventTypes.OnHold || e.EventType == TripEventTypes.Cancelled))
            .GroupBy(e => e.TripId).Select(g => new { TripId = g.Key, Last = g.OrderByDescending(e => e.EventDateTime).First() }).ToDictionaryAsync(g => g.TripId, g => g.Last, ct);
        var userIds = lastEvents.Values.Select(e => e.UserId).Distinct().ToList();
        var userInfos = await partners.FindManyAsync(userIds, ct);

        var rows = trips.Select(t =>
        {
            var evt = lastEvents.GetValueOrDefault(t.TripId);
            return new Dictionary<string, object?>
            {
                ["tripNumber"] = t.TripNumber, ["status"] = t.Status, ["reason"] = t.Status == TripStatuses.OnHold ? t.HoldReason : t.CancelReason,
                ["by"] = evt is not null ? userInfos.GetValueOrDefault(evt.UserId)?.DisplayName : null, ["since"] = evt?.EventDateTime
            };
        }).ToList();
        return (columns, rows);
    }

    private async Task<(List<string>, List<Dictionary<string, object?>>)> Trp11Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "tripNumber", "inactiveReason", "onInvoice" };
        var trips = await db.Trips.AsNoTracking().Where(t => t.TenantId == tenant.TenantId && !t.IsActive
            && (f.From == null || t.TripDate >= f.From) && (f.To == null || t.TripDate <= f.To)).ToListAsync(ct);
        var rows = trips.Select(t => new Dictionary<string, object?>
        { ["tripNumber"] = t.TripNumber, ["inactiveReason"] = t.InactiveReason, ["onInvoice"] = t.InvoiceId != null }).ToList();
        return (columns, rows);
    }

    private async Task<(List<string>, List<Dictionary<string, object?>>)> Trp12Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "tripNumber", "deliveredOn", "podStatus", "daysPending" };
        var today = await clock.TodayAsync(tenant.TenantId);
        var trips = await FilteredTrips(f).Where(t => t.Status == TripStatuses.Delivered || t.Status == TripStatuses.Completed).ToListAsync(ct);
        if (trips.Count == 0) return (columns, []);

        var tripIds = trips.Select(t => t.TripId).ToList();
        var latestPods = await db.TripPODs.AsNoTracking().Where(p => p.TenantId == tenant.TenantId && tripIds.Contains(p.TripId))
            .GroupBy(p => p.TripId).Select(g => g.OrderByDescending(p => p.UploadedAtUtc).First()).ToDictionaryAsync(p => p.TripId, ct);

        var rows = trips.Select(t =>
        {
            var pod = latestPods.GetValueOrDefault(t.TripId);
            var deliveredOn = t.CompletionDate ?? DateOnly.FromDateTime(t.ActualEnd ?? DateTime.UtcNow);
            return new Dictionary<string, object?>
            {
                ["tripNumber"] = t.TripNumber, ["deliveredOn"] = deliveredOn, ["podStatus"] = pod?.Status ?? "None",
                ["daysPending"] = pod is null ? today.DayNumber - deliveredOn.DayNumber : null
            };
        }).ToList();
        return (columns, rows);
    }

    private async Task<(List<string>, List<Dictionary<string, object?>>)> Trp13Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "tripNumber", "tripDate", "tripConfiguration", "error" };
        var trips = await FilteredTrips(f).Where(t => t.RateMissing).ToListAsync(ct);
        if (trips.Count == 0) return (columns, []);
        var configIds = trips.Where(t => t.TripConfigurationId != null).Select(t => t.TripConfigurationId!.Value).Distinct().ToList();
        var configCodes = configIds.Count == 0 ? new Dictionary<long, string>() : await db.TripConfigurations.AsNoTracking()
            .Where(c => c.TenantId == tenant.TenantId && configIds.Contains(c.TripConfigurationId)).ToDictionaryAsync(c => c.TripConfigurationId, c => c.TripCode, ct);

        var rows = trips.Select(t => new Dictionary<string, object?>
        {
            ["tripNumber"] = t.TripNumber, ["tripDate"] = t.TripDate, ["tripConfiguration"] = t.TripConfigurationId is { } id ? configCodes.GetValueOrDefault(id) : null,
            ["error"] = "No rate covers this trip's date."
        }).ToList();
        return (columns, rows);
    }

    private async Task<(List<string>, List<Dictionary<string, object?>>)> Trp14Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "customerCode", "reference", "tripCount", "tripNumbers" };
        var trips = await FilteredTrips(f).Where(t => t.CustomerTripReference != null).ToListAsync(ct);
        if (trips.Count == 0) return (columns, []);
        var codes = await CustomerCodesAsync(trips.Select(t => t.CustomerId), ct);

        var rows = trips.GroupBy(t => (t.CustomerId, t.CustomerTripReference)).Where(g => g.Count() > 1).Select(g => new Dictionary<string, object?>
        {
            ["customerCode"] = codes.GetValueOrDefault(g.Key.CustomerId), ["reference"] = g.Key.CustomerTripReference,
            ["tripCount"] = g.Count(), ["tripNumbers"] = string.Join(", ", g.Select(t => t.TripNumber))
        }).ToList();
        return (columns, rows);
    }

    private async Task<(List<string>, List<Dictionary<string, object?>>)> Trp15Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "tripNumber", "vehicleRegNo", "defaultDriverName", "actualDriverName", "reason" };
        var trips = await FilteredTrips(f).Where(t => t.IsDriverOverridden).ToListAsync(ct);
        if (trips.Count == 0) return (columns, []);
        var vehicleInfos = await vehicles.FindManyAsync(trips.Select(t => t.VehicleId).Distinct(), ct);
        var driverIds = trips.Select(t => t.DefaultDriverId).Concat(trips.Select(t => t.DriverId)).Where(id => id != null).Select(id => id!.Value).Distinct();
        var driverInfos = await partners.FindManyAsync(driverIds, ct);

        var rows = trips.Select(t => new Dictionary<string, object?>
        {
            ["tripNumber"] = t.TripNumber, ["vehicleRegNo"] = vehicleInfos.GetValueOrDefault(t.VehicleId)?.RegistrationNo,
            ["defaultDriverName"] = t.DefaultDriverId is { } dd ? driverInfos.GetValueOrDefault(dd)?.DisplayName : null,
            ["actualDriverName"] = t.DriverId is { } ad ? driverInfos.GetValueOrDefault(ad)?.DisplayName : null, ["reason"] = t.DriverOverrideReason
        }).ToList();
        return (columns, rows);
    }

    private async Task<(List<string>, List<Dictionary<string, object?>>)> Trp16Async(ReportFilter f, CancellationToken ct)
    {
        var columns = new List<string> { "tripNumber", "issueType", "severity", "reportedBy", "resolved" };
        var issues = await db.TripIssues.AsNoTracking().Where(i => i.TenantId == tenant.TenantId
            && (f.From == null || i.ReportedAtUtc >= f.From.Value.ToDateTime(TimeOnly.MinValue)) && (f.To == null || i.ReportedAtUtc <= f.To.Value.ToDateTime(TimeOnly.MaxValue))
            && (string.IsNullOrEmpty(f.Status) || i.IssueType == f.Status)).ToListAsync(ct);
        if (issues.Count == 0) return (columns, []);

        var tripIds = issues.Select(i => i.TripId).Distinct().ToList();
        var tripNumbers = await db.Trips.AsNoTracking().Where(t => t.TenantId == tenant.TenantId && tripIds.Contains(t.TripId)).ToDictionaryAsync(t => t.TripId, t => t.TripNumber, ct);
        var userInfos = await partners.FindManyAsync(issues.Select(i => i.ReportedBy).Distinct(), ct);

        var rows = issues.Select(i => new Dictionary<string, object?>
        {
            ["tripNumber"] = tripNumbers.GetValueOrDefault(i.TripId), ["issueType"] = i.IssueType, ["severity"] = i.Severity,
            ["reportedBy"] = userInfos.GetValueOrDefault(i.ReportedBy)?.DisplayName, ["resolved"] = i.IsResolved
        }).ToList();
        return (columns, rows);
    }

    /// <summary>The shape TRP-01 and (once filtered further) several other reports need — one place that resolves
    /// vehicle/driver names and route/configuration/invoice labels for a set of trips.</summary>
    private async Task<List<Dictionary<string, object?>>> ToTripRowsAsync(List<Trip> trips, CancellationToken ct)
    {
        var codes = await CustomerCodesAsync(trips.Select(t => t.CustomerId), ct);
        var vehicleInfos = await vehicles.FindManyAsync(trips.Select(t => t.VehicleId).Distinct(), ct);
        var driverInfos = await partners.FindManyAsync(trips.Where(t => t.DriverId != null).Select(t => t.DriverId!.Value).Distinct(), ct);
        var configIds = trips.Where(t => t.TripConfigurationId != null).Select(t => t.TripConfigurationId!.Value).Distinct().ToList();
        var configCodes = configIds.Count == 0 ? new Dictionary<long, string>() : await db.TripConfigurations.AsNoTracking()
            .Where(c => c.TenantId == tenant.TenantId && configIds.Contains(c.TripConfigurationId)).ToDictionaryAsync(c => c.TripConfigurationId, c => c.TripCode, ct);
        var invoiceIds = trips.Where(t => t.InvoiceId != null).Select(t => t.InvoiceId!.Value).Distinct().ToList();
        var invoiceNumbers = invoiceIds.Count == 0 ? new Dictionary<long, string>() : await db.Invoices.AsNoTracking()
            .Where(i => i.TenantId == tenant.TenantId && invoiceIds.Contains(i.InvoiceId)).ToDictionaryAsync(i => i.InvoiceId, i => i.InvoiceNumber, ct);

        return trips.Select(t => new Dictionary<string, object?>
        {
            ["tripNumber"] = t.TripNumber, ["tripDate"] = t.TripDate, ["tripType"] = t.TripType, ["customerCode"] = codes.GetValueOrDefault(t.CustomerId),
            ["customerTripReference"] = t.CustomerTripReference, ["configOrRoute"] = t.TripConfigurationId is { } id ? configCodes.GetValueOrDefault(id) : "Open",
            ["vehicleRegNo"] = vehicleInfos.GetValueOrDefault(t.VehicleId)?.RegistrationNo, ["driverName"] = t.DriverId is { } d ? driverInfos.GetValueOrDefault(d)?.DisplayName : null,
            ["status"] = t.Status, ["isActive"] = t.IsActive, ["tripAmount"] = t.TripAmount,
            ["invoiceNumber"] = t.InvoiceId is { } inv ? invoiceNumbers.GetValueOrDefault(inv) : null
        }).ToList();
    }
}
