using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using VMS.Modules.Vehicles.Domain;
using VMS.Modules.Vehicles.Models;
using VMS.Shared.Exceptions;
using VMS.Shared.Messages;
using VMS.Shared.Numbering;
using VMS.Shared.Partners;

namespace VMS.Modules.Vehicles.Services;

public interface IOdometerService
{
    Task<List<OdometerModel>> ListAsync(int vehicleId);
    Task<OdometerModel> AddAsync(int vehicleId, AddOdometerRequest request, VehicleCaller caller);
}

/// <summary>Odometer readings (FSD §16.3). The opening reading is the floor for every later one (BR-VH-025), and readings never run backwards in time.</summary>
internal sealed class OdometerService(VehicleContext ctx) : IOdometerService
{
    public async Task<List<OdometerModel>> ListAsync(int vehicleId)
    {
        await ctx.LoadReadOnlyAsync(vehicleId);
        return await ctx.Db.Odometer.AsNoTracking().Where(r => r.TenantId == ctx.Tenant && r.VehicleId == vehicleId)
            .OrderByDescending(r => r.ReadingDate).ThenByDescending(r => r.Km)
            .Select(r => new OdometerModel { Id = r.OdometerReadingId, ReadingDate = r.ReadingDate, Km = r.Km, Source = r.Source, Notes = r.Notes }).ToListAsync();
    }

    public async Task<OdometerModel> AddAsync(int vehicleId, AddOdometerRequest request, VehicleCaller caller)
    {
        var vehicle = await ctx.LoadAsync(vehicleId);
        if (ctx.NotInFleet(vehicle, "given an odometer reading") is { } wrong) throw wrong;

        var today = await ctx.TodayAsync();
        var date = request.ReadingDate ?? today;
        var notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        var errors = new List<ValidationError>();
        void Add(string field, string code, params (string Name, object? Value)[] values) => errors.Add(ctx.Messages.Error(field, code, values));

        if (request.Km < 0 || request.Km > 9_999_999) Add("km", Msg.Invalid, ("Field", "Odometer"));
        if (date > today) Add("readingDate", Msg.NotFuture, ("Field", "Reading date"));
        if (notes is { Length: > 250 }) Add("notes", Msg.MaxLength, ("Field", "Notes"), ("Max", 250));
        if (errors.Count > 0) throw new ValidationException(errors);

        // BR-VH-025: never below the opening reading.
        if (vehicle.OpeningOdometer is { } floor && request.Km < floor) Add("km", Msg.VhOdometerBelowOpening, ("Floor", floor));
        if (vehicle.OpeningOdometerDate is { } openedOn && date < openedOn) Add("readingDate", Msg.Min, ("Field", "Reading date"), ("Min", openedOn.ToString("yyyy-MM-dd")));

        // And in step with the others: no lower than the latest reading on or before the day, no higher than the earliest after it.
        var readings = await ctx.Db.Odometer.AsNoTracking().Where(r => r.TenantId == ctx.Tenant && r.VehicleId == vehicleId).ToListAsync();
        var before = readings.Where(r => r.ReadingDate <= date).OrderByDescending(r => r.ReadingDate).ThenByDescending(r => r.Km).FirstOrDefault();
        var after = readings.Where(r => r.ReadingDate > date).OrderBy(r => r.ReadingDate).ThenBy(r => r.Km).FirstOrDefault();
        if ((before is not null && request.Km < before.Km) || (after is not null && request.Km > after.Km))
            Add("km", Msg.VhOdometerOutOfSequence, ("Km", request.Km), ("Date", date.ToString("yyyy-MM-dd")),
                ("Lower", before?.Km.ToString() ?? "no"), ("Upper", after?.Km.ToString() ?? "no"));
        if (errors.Count > 0) throw new ValidationException(errors);

        var reading = new OdometerReading { VehicleId = vehicleId, ReadingDate = date, Km = request.Km, Source = OdometerSources.Manual, Notes = notes, CreatedBy = caller.UserId, CreatedOn = DateTime.UtcNow };
        ctx.Db.Odometer.Add(reading);
        await ctx.Db.SaveChangesAsync();
        return new OdometerModel { Id = reading.OdometerReadingId, ReadingDate = reading.ReadingDate, Km = reading.Km, Source = reading.Source, Notes = reading.Notes };
    }
}

public interface IAssignmentService
{
    Task<VehicleModel> AssignAsync(int vehicleId, AssignDriverRequest request, VehicleCaller caller);
    Task<VehicleModel> ReleaseAsync(int vehicleId, ReleaseDriverRequest request, VehicleCaller caller);
}

/// <summary>
/// A vehicle's default driver (BR-VH-022): one driver per vehicle and one vehicle per driver, at a time. Assigning a driver who
/// already has a vehicle asks first (VAL-VH-017), and only then takes him from it.
/// </summary>
internal sealed class AssignmentService(VehicleContext ctx, IVehicleService vehicles) : IAssignmentService
{
    public async Task<VehicleModel> AssignAsync(int vehicleId, AssignDriverRequest request, VehicleCaller caller)
    {
        var vehicle = await ctx.LoadAsync(vehicleId);
        if (ctx.NotInFleet(vehicle, "given a driver") is { } wrong) throw wrong;   // FR-VH-001: a Draft cannot be assigned

        var today = await ctx.TodayAsync();
        var date = request.EffectiveDate ?? today;
        var errors = new List<ValidationError>();
        if (date > today) errors.Add(ctx.Messages.Error("effectiveDate", Msg.NotFuture, ("Field", "Effective date")));

        var driver = request.DriverId > 0 ? await ctx.Partners.FindAsync(request.DriverId) : null;
        if (driver is null || !driver.IsAvailable) errors.Add(ctx.Messages.Error("driverId", Msg.Invalid, ("Field", "Driver")));
        else if (!driver.HasRole(PartnerRoleCodes.Driver)) errors.Add(ctx.Messages.Error("driverId", Msg.VhCounterpartyLacksRole, ("PartnerName", driver.DisplayName), ("Role", "Driver")));
        if (errors.Count > 0) throw new ValidationException(errors);

        var openHere = await ctx.Db.Assignments.FirstOrDefaultAsync(a => a.TenantId == ctx.Tenant && a.VehicleId == vehicleId && a.EffectiveTo == null);
        if (openHere?.DriverId == request.DriverId) throw new ConflictException($"{driver!.DisplayName} is already the default driver of this vehicle.");

        var openElsewhere = await ctx.Db.Assignments.FirstOrDefaultAsync(a => a.TenantId == ctx.Tenant && a.DriverId == request.DriverId && a.EffectiveTo == null);
        Vehicle? other = null;
        if (openElsewhere is not null)
        {
            other = await ctx.Db.Vehicles.FirstAsync(v => v.TenantId == ctx.Tenant && v.VehicleId == openElsewhere.VehicleId);
            if (!request.ReleaseFromOther)
                throw new ValidationException(ctx.Messages.Error("driverId", Msg.VhDriverAlreadyAssigned, ("DriverName", driver!.DisplayName), ("RegNo", other.RegistrationNo)));
            if (date < openElsewhere.EffectiveFrom) throw new ValidationException(ctx.Messages.Error("effectiveDate", Msg.Min, ("Field", "Effective date"), ("Min", openElsewhere.EffectiveFrom.ToString("yyyy-MM-dd"))));
        }
        if (openHere is not null && date < openHere.EffectiveFrom)
            throw new ValidationException(ctx.Messages.Error("effectiveDate", Msg.Min, ("Field", "Effective date"), ("Min", openHere.EffectiveFrom.ToString("yyyy-MM-dd"))));

        try
        {
            await ctx.Db.InTransactionAsync(async ct =>
            {
                if (openHere is not null) { openHere.EffectiveTo = date; openHere.EndReason = "Replaced by another driver"; }
                if (openElsewhere is not null && other is not null)
                {
                    openElsewhere.EffectiveTo = date;
                    openElsewhere.EndReason = $"Reassigned to {vehicle.RegistrationNo}";
                    other.DefaultDriverId = null;
                    other.ModifiedBy = caller.UserId;
                    other.ModifiedOn = DateTime.UtcNow;
                }
                await ctx.Db.SaveChangesAsync(ct);   // the old assignments end first: the database allows one open at a time

                ctx.Db.Assignments.Add(new DriverAssignment { VehicleId = vehicleId, DriverId = request.DriverId, EffectiveFrom = date, CreatedBy = caller.UserId, CreatedOn = DateTime.UtcNow });
                vehicle.DefaultDriverId = request.DriverId;
                vehicle.ModifiedBy = caller.UserId;
                vehicle.ModifiedOn = DateTime.UtcNow;
                await ctx.Db.SaveChangesAsync(ct);
            });
        }
        catch (Exception ex) when (ex is DbUpdateConcurrencyException || ex is DbUpdateException { InnerException: SqlException { Number: 2601 or 2627 } })
        {
            // Someone assigned the same driver or vehicle, or ended the assignment being ended, at the same moment.
            throw new ConflictException("The driver or the vehicle was assigned by someone else a moment ago. Reload and try again.");
        }
        return await vehicles.GetAsync(vehicleId);
    }

    public async Task<VehicleModel> ReleaseAsync(int vehicleId, ReleaseDriverRequest request, VehicleCaller caller)
    {
        var vehicle = await ctx.LoadAsync(vehicleId);
        var open = await ctx.Db.Assignments.FirstOrDefaultAsync(a => a.TenantId == ctx.Tenant && a.VehicleId == vehicleId && a.EffectiveTo == null)
            ?? throw new ConflictException("This vehicle has no default driver to release.");

        var today = await ctx.TodayAsync();
        var date = request.Date ?? today;
        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        var errors = new List<ValidationError>();
        if (date > today) errors.Add(ctx.Messages.Error("date", Msg.NotFuture, ("Field", "Date")));
        else if (date < open.EffectiveFrom) errors.Add(ctx.Messages.Error("date", Msg.Min, ("Field", "Date"), ("Min", open.EffectiveFrom.ToString("yyyy-MM-dd"))));
        if (reason is { Length: > 500 }) errors.Add(ctx.Messages.Error("reason", Msg.MaxLength, ("Field", "Reason"), ("Max", 500)));
        if (errors.Count > 0) throw new ValidationException(errors);

        open.EffectiveTo = date;
        open.EndReason = reason ?? "Released";
        vehicle.DefaultDriverId = null;
        vehicle.ModifiedBy = caller.UserId;
        vehicle.ModifiedOn = DateTime.UtcNow;
        await ctx.Db.SaveChangesAsync();
        return await vehicles.GetAsync(vehicleId);
    }
}
