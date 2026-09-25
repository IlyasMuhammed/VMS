using Microsoft.EntityFrameworkCore;
using VMS.Modules.Trips.Data;
using VMS.Modules.Trips.Domain;
using VMS.Modules.Trips.Models;
using VMS.Shared.Common;
using VMS.Shared.Exceptions;
using VMS.Shared.Messages;
using VMS.Shared.Partners;
using VMS.Shared.Time;
using VMS.Shared.Vehicles;

namespace VMS.Modules.Trips.Services;

/// <summary>Fuel Card Management (§28).</summary>
public interface IFuelCardService
{
    Task<FuelCardModel> CreateAsync(CreateFuelCardRequest request, CancellationToken ct = default);
    Task<FuelCardModel> GetAsync(int fuelCardId, CancellationToken ct = default);
    Task<IReadOnlyList<FuelCardModel>> ListAsync(CancellationToken ct = default);
    Task<FuelCardModel> SaveAsync(int fuelCardId, SaveFuelCardRequest request, CancellationToken ct = default);
    Task<FuelCardModel> AssignAsync(int fuelCardId, AssignFuelCardRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<FuelCardAssignmentModel>> AssignmentsAsync(int fuelCardId, CancellationToken ct = default);
}

internal sealed class FuelCardService(TripsDbContext db, ITenantContext tenant, IMessageCatalogue messages, IPartnerDirectory partners, IVehicleDirectory vehicles, IOperatingClock clock)
    : IFuelCardService
{
    public async Task<FuelCardModel> CreateAsync(CreateFuelCardRequest request, CancellationToken ct = default)
    {
        var errors = new List<ValidationError>();
        void Add(string field, string code, params (string Name, object? Value)[] values) => errors.Add(messages.Error(field, code, values));

        var cardNumber = (request.CardNumber ?? string.Empty).Trim();
        if (cardNumber.Length == 0) Add("cardNumber", Msg.Required, ("Field", "Card number"));
        else if (await db.FuelCards.AnyAsync(c => c.TenantId == tenant.TenantId && c.CardNumber == cardNumber, ct))
            Add("cardNumber", Msg.AlreadyUsed, ("Field", "card number"), ("Code", cardNumber), ("Name", "another fuel card"));

        var company = await partners.FindAsync(request.FuelCardCompanyId, ct);
        if (company is null || !company.HasRole(PartnerRoleCodes.FuelCardCompany)) Add("fuelCardCompanyId", Msg.Invalid, ("Field", "Fuel card company"));
        else if (!company.IsAvailable) Add("fuelCardCompanyId", Msg.Invalid, ("Field", "Fuel card company (not Active)"));
        if (errors.Count > 0) throw new ValidationException(errors);

        var card = new FuelCard
        {
            CardNumber = cardNumber, FuelCardCompanyId = request.FuelCardCompanyId, CardHolderName = Trim(request.CardHolderName),
            ExpiryDate = request.ExpiryDate, MonthlyLimit = request.MonthlyLimit, Status = FuelCardStatuses.Active, Remarks = Trim(request.Remarks)
        };
        db.FuelCards.Add(card);
        await db.SaveChangesAsync(ct);
        return ToModel(card);
    }

    public async Task<FuelCardModel> GetAsync(int fuelCardId, CancellationToken ct = default) => ToModel(await Find(fuelCardId, ct));

    public async Task<IReadOnlyList<FuelCardModel>> ListAsync(CancellationToken ct = default) =>
        (await db.FuelCards.AsNoTracking().Where(c => c.TenantId == tenant.TenantId).OrderBy(c => c.FuelCardId).ToListAsync(ct)).Select(ToModel).ToList();

    public async Task<FuelCardModel> SaveAsync(int fuelCardId, SaveFuelCardRequest request, CancellationToken ct = default)
    {
        var card = await Find(fuelCardId, ct);
        if (!FuelCardStatuses.Settable.Contains(request.Status))
            throw new ValidationException(messages.Error("status", Msg.OneOf, ("Field", "Status"), ("Allowed", string.Join(", ", FuelCardStatuses.Settable))));
        RequireRowVersion(request.RowVersion);

        try { db.Entry(card).Property(c => c.RowVersion).OriginalValue = Convert.FromBase64String(request.RowVersion); }
        catch (FormatException) { throw new ValidationException(messages.Error("rowVersion", Msg.Invalid, ("Field", "Row version"))); }

        card.CardHolderName = Trim(request.CardHolderName);
        card.ExpiryDate = request.ExpiryDate;
        card.MonthlyLimit = request.MonthlyLimit;
        card.Status = request.Status;
        card.Remarks = Trim(request.Remarks);

        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw await StaleAsync(fuelCardId, ct); }
        return ToModel(card);
    }

    public async Task<FuelCardModel> AssignAsync(int fuelCardId, AssignFuelCardRequest request, CancellationToken ct = default)
    {
        var card = await Find(fuelCardId, ct);
        var errors = new List<ValidationError>();
        void Add(string field, string code, params (string Name, object? Value)[] values) => errors.Add(messages.Error(field, code, values));

        if (request.VehicleId is null && request.DriverId is null) Add("vehicleId", Msg.Required, ("Field", "Vehicle or driver"));
        if (request.VehicleId is { } vehicleId && await vehicles.FindAsync(vehicleId, ct) is null) Add("vehicleId", Msg.Invalid, ("Field", "Vehicle"));
        if (request.DriverId is { } driverId)
        {
            var driver = await partners.FindAsync(driverId, ct);
            if (driver is null || !driver.HasRole(PartnerRoleCodes.Driver)) Add("driverId", Msg.Invalid, ("Field", "Driver"));
            else if (!driver.IsAvailable) Add("driverId", Msg.Invalid, ("Field", "Driver (not Active)"));
        }
        if (errors.Count > 0) throw new ValidationException(errors);

        RequireRowVersion(request.RowVersion);
        try { db.Entry(card).Property(c => c.RowVersion).OriginalValue = Convert.FromBase64String(request.RowVersion); }
        catch (FormatException) { throw new ValidationException(messages.Error("rowVersion", Msg.Invalid, ("Field", "Row version"))); }

        var assignedFrom = request.AssignedFrom ?? await clock.TodayAsync(tenant.TenantId);
        var current = await db.FuelCardAssignments.FirstOrDefaultAsync(a => a.TenantId == tenant.TenantId && a.FuelCardId == fuelCardId && a.AssignedTo == null, ct);
        if (current is not null)
        {
            // §28: "reassignment closes the current row, opens a new one" — no overlapping assignments per card,
            // kept true structurally rather than by a range-overlap check (there is only ever one open row).
            if (assignedFrom <= current.AssignedFrom)
                throw new ValidationException(messages.Error("assignedFrom", Msg.Invalid, ("Field", "Assigned from (must be after the current assignment's own start date)")));
            current.AssignedTo = assignedFrom.AddDays(-1);
        }

        var assignment = new FuelCardAssignment
        {
            FuelCardId = fuelCardId, VehicleId = request.VehicleId, DriverId = request.DriverId, AssignedFrom = assignedFrom, Reason = Trim(request.Reason)
        };
        db.FuelCardAssignments.Add(assignment);
        card.VehicleId = request.VehicleId;
        card.DriverId = request.DriverId;

        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw await StaleAsync(fuelCardId, ct); }
        return ToModel(card);
    }

    public async Task<IReadOnlyList<FuelCardAssignmentModel>> AssignmentsAsync(int fuelCardId, CancellationToken ct = default)
    {
        await Find(fuelCardId, ct);
        var rows = await db.FuelCardAssignments.AsNoTracking().Where(a => a.TenantId == tenant.TenantId && a.FuelCardId == fuelCardId)
            .OrderByDescending(a => a.AssignedFrom).ToListAsync(ct);
        return rows.Select(a => new FuelCardAssignmentModel
        {
            FuelCardAssignmentId = a.FuelCardAssignmentId, FuelCardId = a.FuelCardId, VehicleId = a.VehicleId, DriverId = a.DriverId,
            AssignedFrom = a.AssignedFrom, AssignedTo = a.AssignedTo, Reason = a.Reason
        }).ToList();
    }

    private async Task<FuelCard> Find(int fuelCardId, CancellationToken ct) =>
        await db.FuelCards.FirstOrDefaultAsync(c => c.TenantId == tenant.TenantId && c.FuelCardId == fuelCardId, ct)
        ?? throw new NotFoundException($"Fuel card {fuelCardId} was not found.");

    private async Task<Exception> StaleAsync(int fuelCardId, CancellationToken ct)
    {
        var last = await db.Set<VMS.Shared.Auditing.AuditEntry>().AsNoTracking()
            .Where(a => a.TenantId == tenant.TenantId && a.RootEntity == "FuelCard" && a.RootRecordId == fuelCardId.ToString())
            .OrderByDescending(a => a.AuditEntryID).FirstOrDefaultAsync(ct);
        return new ConcurrencyConflictException(messages.Text(Msg.ChangedByAnother, ("User", last?.UserName ?? "another user"),
            ("At", last is null ? "just now" : $"{last.OccurredAt:yyyy-MM-dd HH:mm} UTC")));
    }

    private void RequireRowVersion(string rowVersion)
    {
        if (string.IsNullOrWhiteSpace(rowVersion)) throw new ValidationException(messages.Error("rowVersion", Msg.Required, ("Field", "Row version")));
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static FuelCardModel ToModel(FuelCard c) => new()
    {
        FuelCardId = c.FuelCardId, MaskedCardNumber = Mask(c.CardNumber), FuelCardCompanyId = c.FuelCardCompanyId, VehicleId = c.VehicleId, DriverId = c.DriverId,
        CardHolderName = c.CardHolderName, ExpiryDate = c.ExpiryDate, MonthlyLimit = c.MonthlyLimit, Status = c.Status, Remarks = c.Remarks,
        RowVersion = Convert.ToBase64String(c.RowVersion)
    };

    /// <summary>§28: "displayed masked except last 4 digits." A number of 4 or fewer digits is masked entirely
    /// rather than shown in full — there is nothing left to hide otherwise.</summary>
    private static string Mask(string cardNumber) =>
        cardNumber.Length <= 4 ? new string('*', cardNumber.Length) : new string('*', cardNumber.Length - 4) + cardNumber[^4..];
}
