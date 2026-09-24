using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using VMS.Modules.Vehicles.Domain;
using VMS.Modules.Vehicles.Models;
using VMS.Shared.Auditing;
using VMS.Shared.Authorization;
using VMS.Shared.Exceptions;
using VMS.Shared.Lookups;
using VMS.Shared.Messages;
using VMS.Shared.Numbering;
using VMS.Shared.Partners;

namespace VMS.Modules.Vehicles.Services;

public interface IVehicleService
{
    Task<VehicleModel> GetAsync(int id);
    Task<VehicleModel> CreateAsync(CreateVehicleRequest request, VehicleCaller caller);
    Task<VehicleModel> UpdateAsync(int id, UpdateVehicleRequest request, VehicleCaller caller);
}

/// <summary>Saving a vehicle's identity, technical and operational fields, as a Draft or once it is in the fleet (FSD §16, FR-VH-012).</summary>
internal sealed class VehicleService(VehicleContext ctx, IAuditContext audit, INumberSeries series, ILookupReader lookups) : IVehicleService
{
    private const int MaxDraftDataBytes = 200 * 1024;

    // ── Reading ─────────────────────────────────────────────────────────────────────

    public async Task<VehicleModel> GetAsync(int id)
    {
        var v = await ctx.LoadReadOnlyAsync(id);
        var relations = await ctx.Db.Relations.AsNoTracking().Where(r => r.TenantId == ctx.Tenant && r.VehicleId == id)
            .OrderByDescending(r => r.EffectiveFrom).ThenByDescending(r => r.VehicleRelationId).ToListAsync();
        var refs = await ctx.RefsAsync(relations.Select(r => r.CounterpartyId)
            .Concat([v.CurrentCounterpartyId, v.DefaultDriverId, v.FuelCardCompanyId, v.TrackerCompanyId]));

        return ToModel(v, relations, refs);
    }

    internal static VehicleModel ToModel(Vehicle v, IReadOnlyList<VehicleRelation> relations, IReadOnlyDictionary<int, PartnerRef> refs)
    {
        var history = relations.Select(r => new RelationModel
        {
            Id = r.VehicleRelationId, Category = r.Category, Counterparty = VehicleContext.Ref(refs, r.CounterpartyId), EffectiveFrom = r.EffectiveFrom,
            EffectiveTo = r.EffectiveTo, AgreementEndDate = r.AgreementEndDate, AgreementReference = r.AgreementReference, SharePercent = r.SharePercent,
            SharingBasis = r.SharingBasis, FixedMonthlyAmount = r.FixedMonthlyAmount, ExpenseSharingRule = r.ExpenseSharingRule, RentAmount = r.RentAmount,
            RentFrequency = r.RentFrequency, RentDueDay = r.RentDueDay, SecurityDeposit = r.SecurityDeposit, ArrangementType = r.ArrangementType,
            AgreedAmount = r.AgreedAmount, RevenueSharePercent = r.RevenueSharePercent
        }).ToList();

        return new VehicleModel
        {
            Id = v.VehicleId, VehicleCode = v.VehicleCode, Status = v.Status, CurrentCategory = v.CurrentCategory,
            CurrentCounterparty = VehicleContext.Ref(refs, v.CurrentCounterpartyId), DefaultDriver = VehicleContext.Ref(refs, v.DefaultDriverId),
            FuelCardCompany = VehicleContext.Ref(refs, v.FuelCardCompanyId), TrackerCompany = VehicleContext.Ref(refs, v.TrackerCompanyId),
            Relation = history.FirstOrDefault(r => r.EffectiveTo is null), RelationHistory = history,
            RegistrationNo = v.RegistrationNo, RegistrationCityId = v.RegistrationCityId, ChassisNo = v.ChassisNo, EngineNo = v.EngineNo,
            VehicleTypeId = v.VehicleTypeId, MakeId = v.MakeId, Model = v.Model, ManufacturingYear = v.ManufacturingYear, Colour = v.Colour,
            FuelType = v.FuelType, TankCapacity = v.TankCapacity, LoadCapacity = v.LoadCapacity, CapacityUnit = v.CapacityUnit,
            AxleConfigurationId = v.AxleConfigurationId, BodyTypeId = v.BodyTypeId, TyreCount = v.TyreCount, Gvw = v.Gvw, BranchId = v.BranchId,
            OpeningOdometer = v.OpeningOdometer, OpeningOdometerDate = v.OpeningOdometerDate, DefaultDriverId = v.DefaultDriverId,
            FuelCardCompanyId = v.FuelCardCompanyId, FuelCardNumber = v.FuelCardNumber, TrackerCompanyId = v.TrackerCompanyId, TrackerDeviceId = v.TrackerDeviceId,
            Remarks = v.Remarks, AcquisitionDate = v.AcquisitionDate, AcquisitionType = v.AcquisitionType, DraftData = v.DraftData,
            CreatedOn = v.CreatedOn, ModifiedOn = v.ModifiedOn, RowVersion = Convert.ToBase64String(v.RowVersion)
        };
    }

    // ── Normalising and checking what was sent ──────────────────────────────────────

    private static string? Blank(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    internal static void Normalize(VehicleInput i)
    {
        i.RegistrationNo = RegistrationNumber.Display(i.RegistrationNo);
        i.ChassisNo = Blank(i.ChassisNo)?.ToUpperInvariant();
        i.EngineNo = Blank(i.EngineNo)?.ToUpperInvariant();
        i.Model = i.Model?.Trim() ?? string.Empty;
        i.Colour = Blank(i.Colour);
        i.FuelType = i.FuelType?.Trim() ?? string.Empty;
        i.CapacityUnit = Blank(i.CapacityUnit);
        i.FuelCardNumber = Blank(i.FuelCardNumber);
        i.TrackerDeviceId = Blank(i.TrackerDeviceId);
        i.Remarks = Blank(i.Remarks);
        i.AcquisitionType = Blank(i.AcquisitionType);
        i.DraftData = Blank(i.DraftData);
    }

    /// <summary>Every check that needs nothing but the input (FSD §16): required fields, lengths, ranges, and the fields that depend on others.</summary>
    internal List<ValidationError> Validate(VehicleInput i, DateOnly today)
    {
        var errors = new List<ValidationError>();
        void Add(string field, string code, params (string Name, object? Value)[] values) => errors.Add(ctx.Messages.Error(field, code, values));
        void TooLong(string field, string label, int max) => Add(field, Msg.MaxLength, ("Field", label), ("Max", max));

        if (i.RegistrationNo.Length == 0) Add("registrationNo", Msg.VhRegNoRequired);
        else if (i.RegistrationNo.Length > 20) TooLong("registrationNo", "Registration number", 20);
        if (i.ChassisNo is { Length: > 30 }) TooLong("chassisNo", "Chassis number", 30);
        if (i.EngineNo is { Length: > 30 }) TooLong("engineNo", "Engine number", 30);
        if (i.VehicleTypeId <= 0) Add("vehicleTypeId", Msg.Required, ("Field", "Vehicle type"));
        if (i.MakeId <= 0) Add("makeId", Msg.Required, ("Field", "Make"));
        if (i.Model.Length == 0) Add("model", Msg.Required, ("Field", "Model"));
        else if (i.Model.Length > 60) TooLong("model", "Model", 60);
        if (i.ManufacturingYear is { } year && (year < 1950 || year > today.Year + 1)) Add("manufacturingYear", Msg.Invalid, ("Field", "Manufacturing year"));
        if (i.Colour is { Length: > 30 }) TooLong("colour", "Colour", 30);

        if (!FuelTypes.All.Contains(i.FuelType)) Add("fuelType", Msg.OneOf, ("Field", "Fuel type"), ("Allowed", string.Join(", ", FuelTypes.All)));
        if (i.TankCapacity < 0 || i.TankCapacity >= 100_000_000m) Add("tankCapacity", Msg.Invalid, ("Field", "Tank capacity"));
        if (i.LoadCapacity < 0 || i.LoadCapacity >= 100_000_000m) Add("loadCapacity", Msg.Invalid, ("Field", "Load capacity"));
        if (i.LoadCapacity is not null && i.CapacityUnit is null) Add("capacityUnit", Msg.Required, ("Field", "Capacity unit"));
        if (i.CapacityUnit is not null && !CapacityUnits.All.Contains(i.CapacityUnit)) Add("capacityUnit", Msg.OneOf, ("Field", "Capacity unit"), ("Allowed", string.Join(", ", CapacityUnits.All)));
        if (i.TyreCount is { } tyres && (tyres < 2 || tyres > 22)) Add("tyreCount", Msg.Invalid, ("Field", "Tyre count"));
        if (i.Gvw < 0 || i.Gvw >= 100_000_000m) Add("gvw", Msg.Invalid, ("Field", "GVW"));

        if (i.OpeningOdometer < 0) Add("openingOdometer", Msg.Min, ("Field", "Opening odometer"), ("Min", 0));
        if (i.OpeningOdometer is not null && i.OpeningOdometerDate is null) Add("openingOdometerDate", Msg.Required, ("Field", "Opening odometer date"));
        if (i.OpeningOdometerDate > today) Add("openingOdometerDate", Msg.NotFuture, ("Field", "Opening odometer date"));

        if (i.FuelCardCompanyId is not null && i.FuelCardNumber is null) Add("fuelCardNumber", Msg.Required, ("Field", "Fuel card number"));
        if (i.FuelCardNumber is { Length: > 30 }) TooLong("fuelCardNumber", "Fuel card number", 30);
        if (i.TrackerDeviceId is { Length: > 40 }) TooLong("trackerDeviceId", "Tracker device ID", 40);
        if (i.Remarks is { Length: > 1000 }) TooLong("remarks", "Remarks", 1000);

        if (i.AcquisitionDate > today) Add("acquisitionDate", Msg.VhAcquisitionDateFuture);
        if (i.AcquisitionType is not null && !AcquisitionTypes.All.Contains(i.AcquisitionType))
            Add("acquisitionType", Msg.OneOf, ("Field", "Acquisition type"), ("Allowed", string.Join(", ", AcquisitionTypes.All)));
        if (i.DraftData is not null && System.Text.Encoding.UTF8.GetByteCount(i.DraftData) > MaxDraftDataBytes) Add("draftData", Msg.Invalid, ("Field", "Draft data"));

        return errors;
    }

    /// <summary>The checks that need the master lists and the partners: values that exist and can be chosen, a load capacity for a truck, the right role for each partner.</summary>
    private async Task<List<ValidationError>> CheckReferencesAsync(VehicleInput i, Vehicle? existing)
    {
        var errors = new List<ValidationError>();
        void Add(string field, string code, params (string Name, object? Value)[] values) => errors.Add(ctx.Messages.Error(field, code, values));

        async Task<LookupItem?> Pick(string type, int? id, string field, string label, int? held)
        {
            if (id is not > 0) return null;
            var item = await lookups.FindAsync(type, id.Value);
            // A value the vehicle already holds stays valid even after it is retired from the list.
            if (item is null || (!item.IsActive && held != id)) Add(field, Msg.Invalid, ("Field", label));
            return item;
        }

        var type = await Pick(PlatformLookups.VehicleType, i.VehicleTypeId, "vehicleTypeId", "Vehicle type", existing?.VehicleTypeId);
        await Pick(PlatformLookups.Make, i.MakeId, "makeId", "Make", existing?.MakeId);
        await Pick(PlatformLookups.City, i.RegistrationCityId, "registrationCityId", "Registration city", existing?.RegistrationCityId);
        await Pick(PlatformLookups.AxleConfiguration, i.AxleConfigurationId, "axleConfigurationId", "Axle configuration", existing?.AxleConfigurationId);
        await Pick(PlatformLookups.BodyType, i.BodyTypeId, "bodyTypeId", "Body type", existing?.BodyTypeId);

        // FR-VH-003: the type decides whether a load capacity is needed.
        if (type is not null && CapacityVehicleTypes.Codes.Contains(type.Code) && i.LoadCapacity is null)
            Add("loadCapacity", Msg.VhCapacityRequired, ("VehicleType", type.Description));

        async Task Partner(int? id, int? held, string field, string role, string roleLabel)
        {
            if (id is not > 0) return;
            var partner = await ctx.Partners.FindAsync(id.Value);
            if (partner is null) { Add(field, Msg.Invalid, ("Field", roleLabel)); return; }
            if (held == id) return;   // already on the record: it may have gone Inactive since
            if (!partner.IsAvailable) Add(field, Msg.Invalid, ("Field", roleLabel));
            else if (!partner.HasRole(role)) Add(field, Msg.VhCounterpartyLacksRole, ("PartnerName", partner.DisplayName), ("Role", roleLabel));
        }

        await Partner(i.DefaultDriverId, existing?.DefaultDriverId, "defaultDriverId", PartnerRoleCodes.Driver, "Driver");
        await Partner(i.FuelCardCompanyId, existing?.FuelCardCompanyId, "fuelCardCompanyId", PartnerRoleCodes.FuelCardCompany, "Fuel Card Company");
        await Partner(i.TrackerCompanyId, existing?.TrackerCompanyId, "trackerCompanyId", PartnerRoleCodes.TrackerCompany, "Tracker Company");

        if (i.BranchId is { } branchId && branchId != existing?.BranchId)
        {
            var branch = await ctx.Branches.FindAsync(branchId);
            if (branch is null || !branch.IsAvailable) Add("branchId", Msg.Invalid, ("Field", "Branch"));
        }
        return errors;
    }

    /// <summary>A registration, chassis or engine number another live vehicle already has (BR-VH-017, FSD §16.1). Each names the vehicle so it can be opened.</summary>
    private async Task<List<ValidationError>> UniquenessErrorsAsync(VehicleInput i, int? excludeId)
    {
        var errors = new List<ValidationError>();
        var exclude = excludeId ?? 0;
        var live = ctx.Db.Vehicles.AsNoTracking().Where(v => v.TenantId == ctx.Tenant && v.IsLive && v.VehicleId != exclude);

        var key = RegistrationNumber.Key(i.RegistrationNo);
        if (key.Length > 0 && await live.Where(v => v.RegNoKey == key).Select(v => new { v.VehicleCode }).FirstOrDefaultAsync() is { } byReg)
            errors.Add(ctx.Messages.Error("registrationNo", Msg.VhRegNoExists, ("RegNo", i.RegistrationNo), ("VehicleCode", byReg.VehicleCode)));
        if (i.ChassisNo is { } chassis && await live.Where(v => v.ChassisNo == chassis).Select(v => new { v.VehicleCode }).FirstOrDefaultAsync() is { } byChassis)
            errors.Add(ctx.Messages.Error("chassisNo", Msg.VhChassisOrEngineDuplicate, ("field", "chassis number"), ("VehicleCode", byChassis.VehicleCode)));
        if (i.EngineNo is { } engine && await live.Where(v => v.EngineNo == engine).Select(v => new { v.VehicleCode }).FirstOrDefaultAsync() is { } byEngine)
            errors.Add(ctx.Messages.Error("engineNo", Msg.VhChassisOrEngineDuplicate, ("field", "engine number"), ("VehicleCode", byEngine.VehicleCode)));
        return errors;
    }

    /// <summary>
    /// A fuel card number a vehicle in the fleet already holds (BR-VH-026). Returns the vehicle holding it; the caller either refuses
    /// (VAL-VH-018) or, when the person agreed to reassign it, takes it from that vehicle.
    /// </summary>
    internal Task<Vehicle?> FuelCardHolderAsync(string? number, int excludeId) =>
        number is null
            ? Task.FromResult<Vehicle?>(null)
            : ctx.Db.Vehicles.Where(v => v.TenantId == ctx.Tenant && v.IsInFleet && v.FuelCardNumber == number && v.VehicleId != excludeId).FirstOrDefaultAsync();

    // ── Creating ────────────────────────────────────────────────────────────────────

    public async Task<VehicleModel> CreateAsync(CreateVehicleRequest request, VehicleCaller caller)
    {
        Normalize(request);
        var today = await ctx.TodayAsync();

        var errors = Validate(request, today);
        errors.AddRange(await CheckReferencesAsync(request, existing: null));
        if (errors.Count > 0) throw new ValidationException(errors);
        errors.AddRange(await UniquenessErrorsAsync(request, excludeId: null));
        if (errors.Count > 0) throw new ValidationException(errors);

        try
        {
            var id = await ctx.Db.InTransactionAsync(async ct =>
            {
                var now = DateTime.UtcNow;
                var vehicle = new Vehicle
                {
                    VehicleCode = await series.NextAsync(ctx.Db, NumberSeriesCodes.Vehicle, today, ctx.Tenant, ct),
                    Status = VehicleStatuses.Draft, CreatedBy = caller.UserId, CreatedOn = now, ModifiedOn = now
                };
                Apply(vehicle, request, caller, creating: true);
                ctx.Db.Vehicles.Add(vehicle);
                await ctx.Db.SaveChangesAsync(ct);

                ctx.LogEvent(vehicle, LifecycleEvents.Created, today, caller, toStatus: VehicleStatuses.Draft);
                if (vehicle.OpeningOdometer is { } km)
                    ctx.Db.Odometer.Add(new OdometerReading
                    {
                        VehicleId = vehicle.VehicleId, ReadingDate = vehicle.OpeningOdometerDate!.Value, Km = km, Source = OdometerSources.VehicleCreation,
                        CreatedBy = caller.UserId, CreatedOn = now
                    });
                await ctx.Db.SaveChangesAsync(ct);
                return vehicle.VehicleId;
            });
            return await GetAsync(id);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            var again = await UniquenessErrorsAsync(request, excludeId: null);
            throw again.Count > 0 ? new ValidationException(again) : ex;
        }
    }

    /// <summary>Copies the sent values onto the vehicle. What the caller may not set, or what only a Draft may hold, is left alone.</summary>
    private static void Apply(Vehicle v, VehicleInput i, VehicleCaller caller, bool creating)
    {
        v.RegistrationNo = i.RegistrationNo;
        v.RegNoKey = RegistrationNumber.Key(i.RegistrationNo);
        v.RegistrationCityId = i.RegistrationCityId;
        v.ChassisNo = i.ChassisNo;
        v.EngineNo = i.EngineNo;
        v.VehicleTypeId = i.VehicleTypeId;
        v.MakeId = i.MakeId;
        v.Model = i.Model;
        v.ManufacturingYear = i.ManufacturingYear;
        v.Colour = i.Colour;
        v.FuelType = i.FuelType;
        v.TankCapacity = i.TankCapacity;
        v.LoadCapacity = i.LoadCapacity;
        v.CapacityUnit = i.LoadCapacity is null ? null : i.CapacityUnit;
        v.AxleConfigurationId = i.AxleConfigurationId;
        v.BodyTypeId = i.BodyTypeId;
        v.TyreCount = i.TyreCount;
        v.Gvw = i.Gvw;
        v.BranchId = i.BranchId;
        v.FuelCardCompanyId = i.FuelCardCompanyId;
        v.FuelCardNumber = i.FuelCardCompanyId is null ? null : i.FuelCardNumber;
        v.TrackerCompanyId = i.TrackerCompanyId;
        v.TrackerDeviceId = i.TrackerDeviceId;
        v.Remarks = i.Remarks;

        if (creating || v.Status == VehicleStatuses.Draft)
        {
            // A Draft holds its default driver as a plain choice; once in the fleet it is changed only by assigning (BR-VH-022).
            v.DefaultDriverId = i.DefaultDriverId;
            v.DraftData = i.DraftData;
        }
        if (caller.Has(PermissionCodes.VEH_ACQUISITION_EDIT))
        {
            v.AcquisitionDate = i.AcquisitionDate;
            v.AcquisitionType = i.AcquisitionType;
        }
        v.OpeningOdometer = i.OpeningOdometer;
        v.OpeningOdometerDate = i.OpeningOdometer is null ? null : i.OpeningOdometerDate;
    }

    // ── Updating ────────────────────────────────────────────────────────────────────

    public async Task<VehicleModel> UpdateAsync(int id, UpdateVehicleRequest request, VehicleCaller caller)
    {
        var vehicle = await ctx.LoadAsync(id);
        if (VehicleStatuses.IsDisposed(vehicle.Status))
            throw new ValidationException(ctx.Messages.Error("status", Msg.VhWrongStatus, ("Status", vehicle.Status), ("Action", "edited")));

        Normalize(request);
        var today = await ctx.TodayAsync();
        var errors = Validate(request, today);
        if (string.IsNullOrWhiteSpace(request.RowVersion)) errors.Add(ctx.Messages.Error("rowVersion", Msg.Required, ("Field", "Row version")));
        errors.AddRange(await CheckReferencesAsync(request, vehicle));

        // The opening reading is the floor for every later one (BR-VH-025), so it is fixed once another reading rests on it.
        var openingChanged = request.OpeningOdometer != vehicle.OpeningOdometer || request.OpeningOdometerDate != vehicle.OpeningOdometerDate;
        var readings = openingChanged
            ? await ctx.Db.Odometer.Where(r => r.TenantId == ctx.Tenant && r.VehicleId == id).ToListAsync()
            : [];
        if (openingChanged && readings.Any(r => r.Source != OdometerSources.VehicleCreation))
            errors.Add(ctx.Messages.Error("openingOdometer", Msg.ReadOnlyOnceSaved, ("Field", "Opening odometer")));
        if (errors.Count > 0) throw new ValidationException(errors);

        errors.AddRange(await UniquenessErrorsAsync(request, id));
        var takeFuelCardFrom = default(Vehicle);
        if (vehicle.IsInFleet && request.FuelCardCompanyId is not null && request.FuelCardNumber != vehicle.FuelCardNumber
            && await FuelCardHolderAsync(request.FuelCardNumber, id) is { } holder)
        {
            if (request.ReassignFuelCard) takeFuelCardFrom = holder;
            else errors.Add(ctx.Messages.Error("fuelCardNumber", Msg.VhFuelCardInUse, ("RegNo", holder.RegistrationNo)));
        }
        if (errors.Count > 0) throw new ValidationException(errors);

        try { ctx.Db.Entry(vehicle).Property(v => v.RowVersion).OriginalValue = Convert.FromBase64String(request.RowVersion); }
        catch (FormatException) { throw new ValidationException(ctx.Messages.Error("rowVersion", Msg.Invalid, ("Field", "Row version"))); }

        try
        {
            await ctx.Db.InTransactionAsync(async ct =>
            {
                var now = DateTime.UtcNow;
                if (takeFuelCardFrom is not null)
                {
                    // Released from the other vehicle first: the database allows one holder at a time.
                    audit.Note(new AuditNote("Vehicle", takeFuelCardFrom.VehicleId.ToString(), "FuelCardReassigned", Field: "FuelCardNumber",
                        OldValue: takeFuelCardFrom.FuelCardNumber, NewValue: null, Reason: $"Reassigned to {vehicle.RegistrationNo}", RootEntity: "Vehicle", RootRecordId: takeFuelCardFrom.VehicleId.ToString()));
                    takeFuelCardFrom.FuelCardCompanyId = null;
                    takeFuelCardFrom.FuelCardNumber = null;
                    takeFuelCardFrom.ModifiedBy = caller.UserId;
                    takeFuelCardFrom.ModifiedOn = now;
                    await ctx.Db.SaveChangesAsync(ct);
                }

                Apply(vehicle, request, caller, creating: false);
                vehicle.ModifiedBy = caller.UserId;
                vehicle.ModifiedOn = now;

                // The creation reading follows the opening odometer while nothing else rests on it.
                var creation = readings.FirstOrDefault(r => r.Source == OdometerSources.VehicleCreation);
                if (openingChanged)
                {
                    if (vehicle.OpeningOdometer is { } km)
                    {
                        if (creation is null)
                            ctx.Db.Odometer.Add(new OdometerReading { VehicleId = id, ReadingDate = vehicle.OpeningOdometerDate!.Value, Km = km, Source = OdometerSources.VehicleCreation, CreatedBy = caller.UserId, CreatedOn = now });
                        else { creation.Km = km; creation.ReadingDate = vehicle.OpeningOdometerDate!.Value; }
                    }
                    else if (creation is not null) ctx.Db.Odometer.Remove(creation);
                }
                await ctx.Db.SaveChangesAsync(ct);
            });
        }
        catch (DbUpdateConcurrencyException) { throw await ctx.StaleAsync(id); }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            var again = await UniquenessErrorsAsync(request, id);
            throw again.Count > 0 ? new ValidationException(again) : ex;
        }

        return await GetAsync(id);
    }
}
