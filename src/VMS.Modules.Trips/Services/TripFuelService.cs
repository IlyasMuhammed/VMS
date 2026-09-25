using Microsoft.EntityFrameworkCore;
using VMS.Modules.Trips.Data;
using VMS.Modules.Trips.Domain;
using VMS.Modules.Trips.Models;
using VMS.Shared.Authorization;
using VMS.Shared.Common;
using VMS.Shared.Exceptions;
using VMS.Shared.Messages;

namespace VMS.Modules.Trips.Services;

/// <summary>Trip fuel (§27, AC-23).</summary>
public interface ITripFuelService
{
    Task<TripFuelListModel> ListAsync(long tripId, TripCaller caller, CancellationToken ct = default);
    Task<TripFuelModel> CreateAsync(long tripId, CreateTripFuelRequest request, TripCaller caller, CancellationToken ct = default);
    Task<TripFuelModel> VoidAsync(long tripFuelId, VoidTripFuelRequest request, int voidedByUserId, CancellationToken ct = default);
}

internal sealed class TripFuelService(TripsDbContext db, ITenantContext tenant, ICallerScope scope, IMessageCatalogue messages, ICurrencyService currency)
    : ITripFuelService
{
    public async Task<TripFuelListModel> ListAsync(long tripId, TripCaller caller, CancellationToken ct = default)
    {
        var trip = await FindTripAsync(tripId, ct);
        TripAccess.RequireViewOrOwnDriver(trip, caller, scope, "Viewing this trip's fuel");
        var entries = await db.TripFuels.AsNoTracking().Where(f => f.TenantId == tenant.TenantId && f.TripId == tripId)
            .OrderByDescending(f => f.FuelDateTime).ToListAsync(ct);
        var live = entries.Where(e => !e.IsVoided).ToList();

        decimal? efficiency = null;
        if (trip.StartOdometer is { } start && trip.EndOdometer is { } end && end > start)
        {
            var totalQuantity = live.Sum(e => e.Quantity);
            if (totalQuantity > 0) efficiency = Math.Round((end - start) / totalQuantity, 2);
        }

        return new TripFuelListModel
        {
            Entries = entries.Select(ToModel).ToList(), TotalQuantity = live.Sum(e => e.Quantity), TotalAmount = live.Sum(e => e.Amount), FuelEfficiencyKmPerLitre = efficiency
        };
    }

    public async Task<TripFuelModel> CreateAsync(long tripId, CreateTripFuelRequest request, TripCaller caller, CancellationToken ct = default)
    {
        var trip = await FindTripAsync(tripId, ct);
        TripAccess.RequireFuelOrOwnDriver(trip, caller, scope, "Logging trip fuel");

        var errors = new List<ValidationError>();
        var warnings = new List<string>();
        void Add(string field, string code, params (string Name, object? Value)[] values) => errors.Add(messages.Error(field, code, values));

        if (!FuelTypes.All.Contains(request.FuelType)) Add("fuelType", Msg.OneOf, ("Field", "Fuel type"), ("Allowed", string.Join(", ", FuelTypes.All)));
        if (request.Quantity <= 0) Add("quantity", Msg.Min, ("Field", "Quantity"), ("Min", "0.001"));
        if (request.Rate <= 0) Add("rate", Msg.Min, ("Field", "Rate"), ("Min", "0.01"));
        if (!FuelPaymentMethods.All.Contains(request.PaymentMethod)) Add("paymentMethod", Msg.OneOf, ("Field", "Payment method"), ("Allowed", string.Join(", ", FuelPaymentMethods.All)));

        // AC-23: fuel card required when the payment method is Fuel Card.
        FuelCard? card = null;
        if (request.PaymentMethod == FuelPaymentMethods.FuelCard)
        {
            if (request.FuelCardId is not { } cardId) Add("fuelCardId", Msg.Required, ("Field", "Fuel card is required when payment method is Fuel Card"));
            else
            {
                card = await db.FuelCards.AsNoTracking().FirstOrDefaultAsync(c => c.TenantId == tenant.TenantId && c.FuelCardId == cardId, ct);
                if (card is null) Add("fuelCardId", Msg.Invalid, ("Field", "Fuel card"));
                else if (card.Status != FuelCardStatuses.Active) Add("fuelCardId", Msg.Invalid, ("Field", "Selected fuel card is expired or inactive"));
            }
        }
        // AC-24's own sibling for Fuel: Other requires its own free-text description.
        if (request.PaymentMethod == FuelPaymentMethods.Other && string.IsNullOrWhiteSpace(request.OtherPaymentText))
            Add("otherPaymentText", Msg.Required, ("Field", "Other payment description"));
        if (errors.Count > 0) throw new ValidationException(errors);

        var fuelDateTime = request.FuelDateTime ?? DateTime.UtcNow;
        if (card is not null)
        {
            // §27: "assigned to the vehicle (or driver) on that date."
            var fuelDate = DateOnly.FromDateTime(fuelDateTime);
            var assigned = await db.FuelCardAssignments.AnyAsync(a => a.TenantId == tenant.TenantId && a.FuelCardId == card.FuelCardId
                && (a.VehicleId == trip.VehicleId || (trip.DriverId != null && a.DriverId == trip.DriverId))
                && a.AssignedFrom <= fuelDate && (a.AssignedTo == null || a.AssignedTo >= fuelDate), ct);
            if (!assigned) throw new ValidationException(messages.Error("fuelCardId", Msg.Invalid, ("Field", "Selected fuel card is not assigned to this trip's vehicle or driver on that date")));
        }

        // §27: "Within trip window ± 24h (warning outside)" — only checkable once the trip has actually started.
        if (trip.ActualStart is { } start && fuelDateTime < start.AddHours(-24)) warnings.Add("Fuel date/time is more than 24 hours before the trip started.");
        if (trip.ActualEnd is { } end && fuelDateTime > end.AddHours(24)) warnings.Add("Fuel date/time is more than 24 hours after the trip ended.");

        if (request.Odometer is { } odometer)
        {
            var previous = await db.TripFuels.AsNoTracking().Where(f => f.TenantId == tenant.TenantId && f.VehicleId == trip.VehicleId && !f.IsVoided && f.Odometer != null)
                .OrderByDescending(f => f.FuelDateTime).Select(f => f.Odometer).FirstOrDefaultAsync(ct);
            if (previous is { } last && odometer < last) warnings.Add($"Odometer ({odometer}) is less than this vehicle's last fuel odometer ({last}).");
        }

        var amount = request.Amount ?? request.Quantity * request.Rate;
        if (Math.Abs(amount - request.Quantity * request.Rate) > 1m) warnings.Add("Amount differs from Quantity × Rate by more than 1.");

        var settings = await currency.GetSettingsAsync(ct);
        var currencyCode = settings.MultiCurrencyEnabled && !string.IsNullOrWhiteSpace(request.CurrencyCode) ? request.CurrencyCode!.Trim().ToUpperInvariant() : settings.BaseCurrencyCode;

        var entry = new TripFuel
        {
            TripId = tripId, VehicleId = trip.VehicleId, FuelDateTime = fuelDateTime, FuelType = request.FuelType, Quantity = request.Quantity, Rate = request.Rate,
            Amount = amount, Odometer = request.Odometer, StationName = Trim(request.StationName), CityId = request.CityId, PaymentMethod = request.PaymentMethod,
            FuelCardId = request.FuelCardId, OtherPaymentText = Trim(request.OtherPaymentText), AttachmentId = request.AttachmentId, Remarks = Trim(request.Remarks),
            CurrencyCode = currencyCode, Source = TripAccess.IsOwnDriver(trip, scope) ? TripEventSources.DriverApp : TripEventSources.Manual
        };
        db.TripFuels.Add(entry);
        await db.SaveChangesAsync(ct);

        var model = ToModel(entry);
        model.Warnings = warnings;
        return model;
    }

    public async Task<TripFuelModel> VoidAsync(long tripFuelId, VoidTripFuelRequest request, int voidedByUserId, CancellationToken ct = default)
    {
        var entry = await db.TripFuels.FirstOrDefaultAsync(f => f.TenantId == tenant.TenantId && f.TripFuelId == tripFuelId, ct)
            ?? throw new NotFoundException($"Trip fuel entry {tripFuelId} was not found.");
        if (entry.IsVoided) throw new ConflictException("This fuel entry is already voided.");
        if (string.IsNullOrWhiteSpace(request.Reason)) throw new ValidationException(messages.Error("reason", Msg.Required, ("Field", "Void reason")));

        entry.IsVoided = true;
        entry.VoidReason = request.Reason.Trim();
        entry.VoidedBy = voidedByUserId;
        entry.VoidedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return ToModel(entry);
    }

    private async Task<Trip> FindTripAsync(long tripId, CancellationToken ct) =>
        await db.Trips.AsNoTracking().FirstOrDefaultAsync(t => t.TenantId == tenant.TenantId && t.TripId == tripId, ct)
        ?? throw new NotFoundException($"Trip {tripId} was not found.");

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static TripFuelModel ToModel(TripFuel f) => new()
    {
        TripFuelId = f.TripFuelId, TripId = f.TripId, VehicleId = f.VehicleId, FuelDateTime = f.FuelDateTime, FuelType = f.FuelType, Quantity = f.Quantity,
        Rate = f.Rate, Amount = f.Amount, Odometer = f.Odometer, StationName = f.StationName, CityId = f.CityId, PaymentMethod = f.PaymentMethod,
        FuelCardId = f.FuelCardId, OtherPaymentText = f.OtherPaymentText, AttachmentId = f.AttachmentId, Remarks = f.Remarks, CurrencyCode = f.CurrencyCode,
        Source = f.Source, IsVoided = f.IsVoided, VoidReason = f.VoidReason, VoidedBy = f.VoidedBy, VoidedAtUtc = f.VoidedAtUtc
    };
}
