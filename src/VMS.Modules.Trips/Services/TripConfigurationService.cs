using Microsoft.EntityFrameworkCore;
using VMS.Modules.Trips.Data;
using VMS.Modules.Trips.Domain;
using VMS.Modules.Trips.Models;
using VMS.Shared.Common;
using VMS.Shared.Exceptions;
using VMS.Shared.Messages;
using VMS.Shared.Numbering;
using VMS.Shared.Time;
using VMS.Shared.Vehicles;

namespace VMS.Modules.Trips.Services;

public interface ITripConfigurationService
{
    Task<IReadOnlyList<TripConfigurationModel>> ListAsync(int customerId, bool includeInactive, CancellationToken ct = default);
    Task<TripConfigurationModel> GetAsync(long configurationId, CancellationToken ct = default);
    Task<TripConfigurationModel> CreateAsync(CreateTripConfigurationRequest request, CancellationToken ct = default);
    Task<TripConfigurationModel> UpdateAsync(long configurationId, UpdateTripConfigurationRequest request, CancellationToken ct = default);
    Task<TripConfigurationModel> UpdateStopsAsync(long configurationId, UpdateTripConfigurationStopsRequest request, CancellationToken ct = default);
    Task<TripConfigurationModel> ActivateAsync(long configurationId, ChangeTripConfigurationStatusRequest request, CancellationToken ct = default);
    Task<TripConfigurationModel> DeactivateAsync(long configurationId, ChangeTripConfigurationStatusRequest request, CancellationToken ct = default);
    Task<TripConfigurationModel> CopyAsync(long configurationId, CopyTripConfigurationRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<TripConfigurationVehicleModel>> ListVehiclesAsync(long configurationId, CancellationToken ct = default);
    Task<TripConfigurationVehicleModel> AssignVehicleAsync(long configurationId, AssignTripConfigurationVehicleRequest request, CancellationToken ct = default);
    Task<TripConfigurationVehicleModel> UpdateVehicleAsync(long assignmentId, UpdateTripConfigurationVehicleRequest request, CancellationToken ct = default);
}

internal sealed class TripConfigurationService(TripsDbContext db, ITenantContext tenant, IMessageCatalogue messages, IOperatingClock clock, IVehicleDirectory vehicles)
    : ITripConfigurationService
{
    public async Task<IReadOnlyList<TripConfigurationModel>> ListAsync(int customerId, bool includeInactive, CancellationToken ct = default)
    {
        // §18: "Configuration list is always filtered by Customer first" — there is deliberately no
        // all-customers list endpoint.
        var query = db.TripConfigurations.AsNoTracking().Where(c => c.TenantId == tenant.TenantId && c.CustomerId == customerId);
        if (!includeInactive) query = query.Where(c => c.Status != TripConfigurationStatuses.Inactive);
        var configs = await query.OrderBy(c => c.TripCode).ToListAsync(ct);
        var stops = await StopsByConfigAsync(configs.Select(c => c.TripConfigurationId), ct);
        return configs.Select(c => ToModel(c, stops.GetValueOrDefault(c.TripConfigurationId, []))).ToList();
    }

    public async Task<TripConfigurationModel> GetAsync(long configurationId, CancellationToken ct = default)
    {
        var config = await Find(configurationId, ct);
        var stops = await db.TripConfigurationStops.AsNoTracking().Where(s => s.TenantId == tenant.TenantId && s.TripConfigurationId == configurationId)
            .OrderBy(s => s.Sequence).Select(s => ToModel(s)).ToListAsync(ct);
        return ToModel(config, stops);
    }

    public async Task<TripConfigurationModel> CreateAsync(CreateTripConfigurationRequest request, CancellationToken ct = default)
    {
        var errors = new List<ValidationError>();
        void Add(string field, string code, params (string Name, object? Value)[] values) => errors.Add(messages.Error(field, code, values));

        var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.TenantId == tenant.TenantId && c.CustomerId == request.CustomerId, ct);
        if (customer is null) Add("customerId", Msg.Invalid, ("Field", "Customer"));
        else if (customer.Status != Domain.CustomerStatuses.Active) throw new BusinessRuleException("CUSTOMER_INACTIVE", "Customer is inactive.",
            [new BusinessRuleDetail("customerId", request.CustomerId, "Only an Active customer can have new trip configurations.")]);

        var route = await db.Routes.AsNoTracking().FirstOrDefaultAsync(r => r.TenantId == tenant.TenantId && r.RouteId == request.RouteId, ct);
        if (route is null) Add("routeId", Msg.Invalid, ("Field", "Route"));
        else if (route.Status != ActiveInactiveStatuses.Active) Add("routeId", Msg.Invalid, ("Field", "Route (inactive)"));

        if (string.IsNullOrWhiteSpace(request.Name)) Add("name", Msg.Required, ("Field", "Name"));
        if (!TripDirectionTypes.All.Contains(request.DirectionType))
            Add("directionType", Msg.OneOf, ("Field", "Direction type"), ("Allowed", string.Join(", ", TripDirectionTypes.All)));

        string? code = null;
        if (!string.IsNullOrWhiteSpace(request.TripCode))
        {
            code = request.TripCode.Trim();
            if (code.Length > 40) Add("tripCode", Msg.MaxLength, ("Field", "Trip code"), ("Max", "40"));
            else if (await db.TripConfigurations.AnyAsync(c => c.TenantId == tenant.TenantId && c.CustomerId == request.CustomerId && c.TripCode == code, ct))
                throw new ValidationException(messages.Error("tripCode", Msg.AlreadyUsed, ("Field", "trip code"), ("Code", code), ("Name", "another configuration for this customer")));
        }
        if (errors.Count > 0) throw new ValidationException(errors);

        code ??= await SuggestTripCodeAsync(customer!, route!, request.DirectionType, ct);
        var routeStops = await db.RouteStops.AsNoTracking().Where(s => s.TenantId == tenant.TenantId && s.RouteId == route!.RouteId).OrderBy(s => s.Sequence).ToListAsync(ct);

        var config = new TripConfiguration
        {
            CustomerId = request.CustomerId, TripCode = code, Name = request.Name.Trim(), RouteId = request.RouteId,
            DirectionType = request.DirectionType, Status = TripConfigurationStatuses.Draft,
            Remarks = string.IsNullOrWhiteSpace(request.Remarks) ? null : request.Remarks.Trim()
        };

        await db.InTransactionAsync(async ct2 =>
        {
            db.TripConfigurations.Add(config);
            await db.SaveChangesAsync(ct2);
            // §18: "On creation stops are copied from the Route and can then be adjusted for this customer."
            db.TripConfigurationStops.AddRange(routeStops.Select(s => new TripConfigurationStop
            {
                TripConfigurationId = config.TripConfigurationId, CityId = s.CityId, Sequence = s.Sequence, StopType = s.StopType
            }));
            await db.SaveChangesAsync(ct2);
        }, ct);

        return await GetAsync(config.TripConfigurationId, ct);
    }

    public async Task<TripConfigurationModel> UpdateAsync(long configurationId, UpdateTripConfigurationRequest request, CancellationToken ct = default)
    {
        var config = await Find(configurationId, ct);
        var errors = new List<ValidationError>();
        void Add(string field, string code, params (string Name, object? Value)[] values) => errors.Add(messages.Error(field, code, values));

        if (string.IsNullOrWhiteSpace(request.Name)) Add("name", Msg.Required, ("Field", "Name"));
        if (!TripDirectionTypes.All.Contains(request.DirectionType))
            Add("directionType", Msg.OneOf, ("Field", "Direction type"), ("Allowed", string.Join(", ", TripDirectionTypes.All)));
        if (string.IsNullOrWhiteSpace(request.RowVersion)) Add("rowVersion", Msg.Required, ("Field", "Row version"));
        if (errors.Count > 0) throw new ValidationException(errors);

        try { db.Entry(config).Property(c => c.RowVersion).OriginalValue = Convert.FromBase64String(request.RowVersion); }
        catch (FormatException) { throw new ValidationException(messages.Error("rowVersion", Msg.Invalid, ("Field", "Row version"))); }

        config.Name = request.Name.Trim();
        config.DirectionType = request.DirectionType;
        config.Remarks = string.IsNullOrWhiteSpace(request.Remarks) ? null : request.Remarks.Trim();
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw await StaleAsync(configurationId, ct); }
        return await GetAsync(configurationId, ct);
    }

    public async Task<TripConfigurationModel> UpdateStopsAsync(long configurationId, UpdateTripConfigurationStopsRequest request, CancellationToken ct = default)
    {
        await Find(configurationId, ct);
        var errors = new List<ValidationError>();
        void Add(string field, string code, params (string Name, object? Value)[] values) => errors.Add(messages.Error(field, code, values));
        await ValidateStopsAsync(request.Stops, Add, ct);
        if (errors.Count > 0) throw new ValidationException(errors);

        await db.InTransactionAsync(async ct2 =>
        {
            var old = await db.TripConfigurationStops.Where(s => s.TenantId == tenant.TenantId && s.TripConfigurationId == configurationId).ToListAsync(ct2);
            db.TripConfigurationStops.RemoveRange(old);
            await db.SaveChangesAsync(ct2);
            db.TripConfigurationStops.AddRange(request.Stops.Select((s, i) => new TripConfigurationStop
            {
                TripConfigurationId = configurationId, CityId = s.CityId, OtherLocation = string.IsNullOrWhiteSpace(s.OtherLocation) ? null : s.OtherLocation.Trim(),
                Sequence = i + 1, StopType = s.StopType
            }));
            await db.SaveChangesAsync(ct2);
        }, ct);

        return await GetAsync(configurationId, ct);
    }

    public async Task<TripConfigurationModel> ActivateAsync(long configurationId, ChangeTripConfigurationStatusRequest request, CancellationToken ct = default)
    {
        var config = await Find(configurationId, ct);
        if (config.Status == TripConfigurationStatuses.Active) throw new ConflictException("This configuration is already Active.");
        if (string.IsNullOrWhiteSpace(request.RowVersion)) throw new ValidationException(messages.Error("rowVersion", Msg.Required, ("Field", "Row version")));

        var hasActiveVehicle = await db.TripConfigurationVehicles.AnyAsync(v => v.TenantId == tenant.TenantId && v.TripConfigurationId == configurationId && v.Status == ActiveInactiveStatuses.Active, ct);
        if (!hasActiveVehicle)
            throw new BusinessRuleException("TRIPCONFIG_NO_ACTIVE_VEHICLE", "A trip configuration needs at least one active vehicle before it can be activated.");
        // §18: "Active needs ... ≥ 1 rate (warning if no rate)" — a warning, never a block (added once TripRate
        // existed, CC-11; matches every other "warning, never a block" rule in this FSD — BR-C2, §11's last-contact
        // warning, etc.).
        var hasActiveRate = await db.TripRates.AnyAsync(r => r.TenantId == tenant.TenantId && r.TripConfigurationId == configurationId && r.Status == ActiveInactiveStatuses.Active, ct);

        try { db.Entry(config).Property(c => c.RowVersion).OriginalValue = Convert.FromBase64String(request.RowVersion); }
        catch (FormatException) { throw new ValidationException(messages.Error("rowVersion", Msg.Invalid, ("Field", "Row version"))); }

        config.Status = TripConfigurationStatuses.Active;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw await StaleAsync(configurationId, ct); }
        var result = await GetAsync(configurationId, ct);
        if (!hasActiveRate) result.Warnings = ["This configuration has no active rate yet — trips on it will be RateMissing until one is added."];
        return result;
    }

    public async Task<TripConfigurationModel> DeactivateAsync(long configurationId, ChangeTripConfigurationStatusRequest request, CancellationToken ct = default)
    {
        var config = await Find(configurationId, ct);
        if (config.Status == TripConfigurationStatuses.Inactive) throw new ConflictException("This configuration is already Inactive.");
        if (string.IsNullOrWhiteSpace(request.RowVersion)) throw new ValidationException(messages.Error("rowVersion", Msg.Required, ("Field", "Row version")));

        try { db.Entry(config).Property(c => c.RowVersion).OriginalValue = Convert.FromBase64String(request.RowVersion); }
        catch (FormatException) { throw new ValidationException(messages.Error("rowVersion", Msg.Invalid, ("Field", "Row version"))); }

        // §18: "Deactivation blocks new trips; existing trips and invoices unaffected" — no other guard needed.
        config.Status = TripConfigurationStatuses.Inactive;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw await StaleAsync(configurationId, ct); }
        return await GetAsync(configurationId, ct);
    }

    public async Task<TripConfigurationModel> CopyAsync(long configurationId, CopyTripConfigurationRequest request, CancellationToken ct = default)
    {
        var source = await Find(configurationId, ct);
        var targetCustomerId = request.CustomerId ?? source.CustomerId;
        var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.TenantId == tenant.TenantId && c.CustomerId == targetCustomerId, ct)
            ?? throw new NotFoundException($"Customer {targetCustomerId} was not found.");
        var route = await db.Routes.AsNoTracking().FirstAsync(r => r.TenantId == tenant.TenantId && r.RouteId == source.RouteId, ct);

        var name = string.IsNullOrWhiteSpace(request.Name) ? $"{source.Name} (copy)" : request.Name.Trim();
        var code = string.IsNullOrWhiteSpace(request.TripCode) ? null : request.TripCode.Trim();
        if (code is not null && await db.TripConfigurations.AnyAsync(c => c.TenantId == tenant.TenantId && c.CustomerId == targetCustomerId && c.TripCode == code, ct))
            throw new ValidationException(messages.Error("tripCode", Msg.AlreadyUsed, ("Field", "trip code"), ("Code", code), ("Name", "another configuration for this customer")));
        code ??= await SuggestTripCodeAsync(customer, route, source.DirectionType, ct);

        var sourceStops = await db.TripConfigurationStops.AsNoTracking().Where(s => s.TenantId == tenant.TenantId && s.TripConfigurationId == configurationId).OrderBy(s => s.Sequence).ToListAsync(ct);
        var sourceVehicles = await db.TripConfigurationVehicles.AsNoTracking().Where(v => v.TenantId == tenant.TenantId && v.TripConfigurationId == configurationId).ToListAsync(ct);

        var copy = new TripConfiguration
        {
            CustomerId = targetCustomerId, TripCode = code, Name = name, RouteId = source.RouteId,
            DirectionType = source.DirectionType, Status = TripConfigurationStatuses.Draft, Remarks = source.Remarks
        };

        await db.InTransactionAsync(async ct2 =>
        {
            db.TripConfigurations.Add(copy);
            await db.SaveChangesAsync(ct2);
            // §18: "'Copy configuration' creates a new configuration ... with stops and vehicles (rates are not
            // copied unless the user ticks 'Copy rates')" — rates don't exist yet (CC-11), so there is nothing to
            // conditionally copy today; stops and vehicles always are.
            db.TripConfigurationStops.AddRange(sourceStops.Select(s => new TripConfigurationStop
            {
                TripConfigurationId = copy.TripConfigurationId, CityId = s.CityId, OtherLocation = s.OtherLocation, Sequence = s.Sequence, StopType = s.StopType
            }));
            db.TripConfigurationVehicles.AddRange(sourceVehicles.Select(v => new TripConfigurationVehicle
            {
                TripConfigurationId = copy.TripConfigurationId, VehicleId = v.VehicleId, EffectiveFrom = v.EffectiveFrom,
                EffectiveTo = v.EffectiveTo, Status = v.Status, Remarks = v.Remarks
            }));
            await db.SaveChangesAsync(ct2);
        }, ct);

        return await GetAsync(copy.TripConfigurationId, ct);
    }

    // ── Vehicles (§19) ──────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<TripConfigurationVehicleModel>> ListVehiclesAsync(long configurationId, CancellationToken ct = default)
    {
        await Find(configurationId, ct);
        var rows = await db.TripConfigurationVehicles.AsNoTracking().Where(v => v.TenantId == tenant.TenantId && v.TripConfigurationId == configurationId)
            .OrderByDescending(v => v.Status == ActiveInactiveStatuses.Active).ThenBy(v => v.EffectiveFrom).ToListAsync(ct);
        var info = await vehicles.FindManyAsync(rows.Select(r => r.VehicleId), ct);
        return rows.Select(r => ToModel(r, info.GetValueOrDefault(r.VehicleId))).ToList();
    }

    public async Task<TripConfigurationVehicleModel> AssignVehicleAsync(long configurationId, AssignTripConfigurationVehicleRequest request, CancellationToken ct = default)
    {
        await Find(configurationId, ct);
        var errors = new List<ValidationError>();
        void Add(string field, string code, params (string Name, object? Value)[] values) => errors.Add(messages.Error(field, code, values));

        var vehicle = await vehicles.FindAsync(request.VehicleId, ct);
        if (vehicle is null) Add("vehicleId", Msg.Invalid, ("Field", "Vehicle"));
        else if (!vehicle.IsOperational) throw new BusinessRuleException("VEHICLE_NOT_OPERATIONAL",
            $"{vehicle.RegistrationNo} is not operational and cannot be assigned to a trip configuration.",
            [new BusinessRuleDetail("vehicleId", request.VehicleId, "Vehicle must be operational (not sold/disposed/under-repair-blocked).")]);

        var effectiveFrom = request.EffectiveFrom ?? await clock.TodayAsync(tenant.TenantId);
        if (request.EffectiveTo is { } to && to < effectiveFrom) Add("effectiveTo", Msg.Min, ("Field", "Effective to"), ("Min", effectiveFrom.ToString("yyyy-MM-dd")));
        if (errors.Count > 0) throw new ValidationException(errors);

        // §19: "Same vehicle cannot have overlapping effective ranges within one configuration."
        var overlapping = await db.TripConfigurationVehicles.AnyAsync(v => v.TenantId == tenant.TenantId && v.TripConfigurationId == configurationId
            && v.VehicleId == request.VehicleId && v.EffectiveFrom <= (request.EffectiveTo ?? DateOnly.MaxValue) && (v.EffectiveTo == null || v.EffectiveTo >= effectiveFrom), ct);
        if (overlapping) throw new ValidationException(messages.Error("effectiveFrom", Msg.Invalid, ("Field", "This vehicle already has an overlapping effective range on this configuration")));

        var row = new TripConfigurationVehicle
        {
            TripConfigurationId = configurationId, VehicleId = request.VehicleId, EffectiveFrom = effectiveFrom, EffectiveTo = request.EffectiveTo,
            Status = ActiveInactiveStatuses.Active, Remarks = string.IsNullOrWhiteSpace(request.Remarks) ? null : request.Remarks.Trim()
        };
        db.TripConfigurationVehicles.Add(row);
        await db.SaveChangesAsync(ct);
        return ToModel(row, vehicle);
    }

    public async Task<TripConfigurationVehicleModel> UpdateVehicleAsync(long assignmentId, UpdateTripConfigurationVehicleRequest request, CancellationToken ct = default)
    {
        var row = await db.TripConfigurationVehicles.FirstOrDefaultAsync(v => v.TenantId == tenant.TenantId && v.TripConfigurationVehicleId == assignmentId, ct)
            ?? throw new NotFoundException($"Trip configuration vehicle {assignmentId} was not found.");

        var status = string.IsNullOrWhiteSpace(request.Status) ? row.Status : request.Status;
        if (status is not (ActiveInactiveStatuses.Active or ActiveInactiveStatuses.Inactive))
            throw new ValidationException(messages.Error("status", Msg.OneOf, ("Field", "Status"), ("Allowed", string.Join(", ", ActiveInactiveStatuses.All))));
        if (request.EffectiveTo is { } to && to < row.EffectiveFrom)
            throw new ValidationException(messages.Error("effectiveTo", Msg.Min, ("Field", "Effective to"), ("Min", row.EffectiveFrom.ToString("yyyy-MM-dd"))));

        row.EffectiveTo = request.EffectiveTo ?? row.EffectiveTo;
        row.Status = status;
        row.Remarks = string.IsNullOrWhiteSpace(request.Remarks) ? row.Remarks : request.Remarks.Trim();
        await db.SaveChangesAsync(ct);
        return ToModel(row, await vehicles.FindAsync(row.VehicleId, ct));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────

    private async Task<TripConfiguration> Find(long configurationId, CancellationToken ct) =>
        await db.TripConfigurations.FirstOrDefaultAsync(c => c.TenantId == tenant.TenantId && c.TripConfigurationId == configurationId, ct)
        ?? throw new NotFoundException($"Trip configuration {configurationId} was not found.");

    private async Task<Exception> StaleAsync(long configurationId, CancellationToken ct)
    {
        var last = await db.Set<VMS.Shared.Auditing.AuditEntry>().AsNoTracking()
            .Where(a => a.TenantId == tenant.TenantId && a.RootEntity == "TripConfiguration" && a.RootRecordId == configurationId.ToString())
            .OrderByDescending(a => a.AuditEntryID).FirstOrDefaultAsync(ct);
        return new ConcurrencyConflictException(messages.Text(Msg.ChangedByAnother, ("User", last?.UserName ?? "another user"),
            ("At", last is null ? "just now" : $"{last.OccurredAt:yyyy-MM-dd HH:mm} UTC")));
    }

    private async Task ValidateStopsAsync(IReadOnlyList<TripConfigurationStopInput> stops, Action<string, string, (string, object?)[]> add, CancellationToken ct)
    {
        if (stops.Count < 2) { add("stops", Msg.Min, [("Field", "Stops"), ("Min", "2")]); return; }

        var cityIds = stops.Where(s => s.CityId is not null).Select(s => s.CityId!.Value).ToList();
        var cities = cityIds.Count == 0 ? new Dictionary<int, City>() : await db.Cities.Where(c => c.TenantId == tenant.TenantId && cityIds.Contains(c.CityId)).ToDictionaryAsync(c => c.CityId, ct);

        for (var i = 0; i < stops.Count; i++)
        {
            var stop = stops[i];
            var hasCity = stop.CityId is not null;
            var hasOther = !string.IsNullOrWhiteSpace(stop.OtherLocation);
            if (hasCity == hasOther) add($"stops[{i}]", Msg.Invalid, [("Field", "Exactly one of city or other location must be set")]);
            else if (hasCity && (!cities.TryGetValue(stop.CityId!.Value, out var city) || city.Status != ActiveInactiveStatuses.Active))
                add($"stops[{i}].cityId", Msg.Invalid, [("Field", "City (unknown or inactive)")]);

            if (!RouteStopTypes.All.Contains(stop.StopType)) add($"stops[{i}].stopType", Msg.OneOf, [("Field", "Stop type"), ("Allowed", string.Join(", ", RouteStopTypes.All))]);
            else if (i == 0 && stop.StopType != RouteStopTypes.Origin) add($"stops[{i}].stopType", Msg.Invalid, [("Field", "First stop must be Origin")]);
            else if (i == stops.Count - 1 && stop.StopType != RouteStopTypes.Destination) add($"stops[{i}].stopType", Msg.Invalid, [("Field", "Last stop must be Destination")]);
        }
    }

    private async Task<string> SuggestTripCodeAsync(Domain.Customer customer, Route route, string directionType, CancellationToken ct)
    {
        var custShort = !string.IsNullOrWhiteSpace(customer.ShortName) ? customer.ShortName! : customer.CustomerName.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        custShort = new string(custShort.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        if (custShort.Length > 8) custShort = custShort[..8];

        var origin = await db.Cities.FirstAsync(c => c.CityId == route.OriginCityId, ct);
        var destination = await db.Cities.FirstAsync(c => c.CityId == route.DestinationCityId, ct);
        var baseCode = $"{custShort}-{origin.Abbreviation}-{destination.Abbreviation}";

        if (directionType == TripDirectionTypes.RoundTrip)
        {
            var rt = $"{baseCode}-RT";
            if (!await db.TripConfigurations.AnyAsync(c => c.TenantId == tenant.TenantId && c.CustomerId == customer.CustomerId && c.TripCode == rt, ct)) return rt;
        }
        var n = 1;
        string candidate;
        do { candidate = $"{baseCode}-{n++:00}"; } while (await db.TripConfigurations.AnyAsync(c => c.TenantId == tenant.TenantId && c.CustomerId == customer.CustomerId && c.TripCode == candidate, ct));
        return candidate;
    }

    private async Task<Dictionary<long, List<TripConfigurationStopModel>>> StopsByConfigAsync(IEnumerable<long> configIds, CancellationToken ct)
    {
        var ids = configIds.ToList();
        var stops = await db.TripConfigurationStops.AsNoTracking().Where(s => s.TenantId == tenant.TenantId && ids.Contains(s.TripConfigurationId))
            .OrderBy(s => s.Sequence).ToListAsync(ct);
        return stops.GroupBy(s => s.TripConfigurationId).ToDictionary(g => g.Key, g => g.Select(ToModel).ToList());
    }

    private static TripConfigurationStopModel ToModel(TripConfigurationStop s) => new()
    { TripConfigurationStopId = s.TripConfigurationStopId, CityId = s.CityId, OtherLocation = s.OtherLocation, Sequence = s.Sequence, StopType = s.StopType };

    private static TripConfigurationModel ToModel(TripConfiguration c, IReadOnlyList<TripConfigurationStopModel> stops) => new()
    {
        TripConfigurationId = c.TripConfigurationId, CustomerId = c.CustomerId, TripCode = c.TripCode, Name = c.Name, RouteId = c.RouteId,
        DirectionType = c.DirectionType, Status = c.Status, Remarks = c.Remarks, RowVersion = Convert.ToBase64String(c.RowVersion), Stops = stops
    };

    private static TripConfigurationVehicleModel ToModel(TripConfigurationVehicle v, VehicleInfo? info) => new()
    {
        TripConfigurationVehicleId = v.TripConfigurationVehicleId, VehicleId = v.VehicleId, VehicleCode = info?.VehicleCode,
        RegistrationNo = info?.RegistrationNo, Category = info?.Category, EffectiveFrom = v.EffectiveFrom, EffectiveTo = v.EffectiveTo,
        Status = v.Status, Remarks = v.Remarks
    };
}
