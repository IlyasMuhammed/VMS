using Microsoft.EntityFrameworkCore;
using VMS.Modules.Trips.Data;
using VMS.Modules.Trips.Domain;
using VMS.Modules.Trips.Models;
using VMS.Shared.Common;
using VMS.Shared.Exceptions;
using VMS.Shared.Lookups;
using VMS.Shared.Messages;
using VMS.Shared.Time;

namespace VMS.Modules.Trips.Services;

/// <summary>Trip income with a billable flag (§30). Not part of the driver-app channel table (§23) — unlike
/// fuel/expenses/issues, every action here is back-office only, so no conditional <c>TripAccess</c> check is
/// needed.</summary>
public interface ITripIncomeService
{
    Task<IReadOnlyList<TripIncomeModel>> ListAsync(long tripId, CancellationToken ct = default);
    Task<TripIncomeModel> CreateAsync(long tripId, CreateTripIncomeRequest request, CancellationToken ct = default);
    Task<TripIncomeModel> VoidAsync(long tripIncomeId, VoidTripIncomeRequest request, int voidedByUserId, CancellationToken ct = default);

    /// <summary>§30/§47.2: "billable income exposed for invoice lines" — the read invoicing (a later CC task)
    /// will need: unvoided, billable, not yet on any invoice.</summary>
    Task<IReadOnlyList<TripIncomeModel>> ListBillableUnbilledAsync(int customerId, CancellationToken ct = default);
}

internal sealed class TripIncomeService(TripsDbContext db, ITenantContext tenant, IMessageCatalogue messages, ILookupReader lookups, ICurrencyService currency, IOperatingClock clock)
    : ITripIncomeService
{
    public async Task<IReadOnlyList<TripIncomeModel>> ListAsync(long tripId, CancellationToken ct = default)
    {
        await FindTripAsync(tripId, ct);
        var rows = await db.TripIncomes.AsNoTracking().Where(i => i.TenantId == tenant.TenantId && i.TripId == tripId).OrderByDescending(i => i.IncomeDate).ToListAsync(ct);
        return rows.Select(ToModel).ToList();
    }

    public async Task<TripIncomeModel> CreateAsync(long tripId, CreateTripIncomeRequest request, CancellationToken ct = default)
    {
        var trip = await FindTripAsync(tripId, ct);
        var errors = new List<ValidationError>();
        void Add(string field, string code, params (string Name, object? Value)[] values) => errors.Add(messages.Error(field, code, values));

        var type = await lookups.FindAsync(PlatformLookups.TripIncomeType, request.IncomeTypeId);
        if (type is null || !type.IsActive) Add("incomeTypeId", Msg.Invalid, ("Field", "Income type"));

        var customerId = request.CustomerId ?? trip.CustomerId;
        if (customerId != trip.CustomerId)
        {
            var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.TenantId == tenant.TenantId && c.CustomerId == customerId, ct);
            if (customer is null || customer.Status != CustomerStatuses.Active) Add("customerId", Msg.Invalid, ("Field", "Customer"));
        }
        if (request.Amount <= 0) Add("amount", Msg.Min, ("Field", "Amount"), ("Min", "0.01"));
        if (errors.Count > 0) throw new ValidationException(errors);

        var settings = await currency.GetSettingsAsync(ct);
        var currencyCode = settings.MultiCurrencyEnabled && !string.IsNullOrWhiteSpace(request.CurrencyCode)
            ? request.CurrencyCode!.Trim().ToUpperInvariant() : trip.CurrencyCode ?? settings.BaseCurrencyCode;

        var income = new TripIncome
        {
            TripId = tripId, CustomerId = customerId, IncomeTypeId = request.IncomeTypeId, Amount = request.Amount, CurrencyCode = currencyCode,
            IncomeDate = request.IncomeDate ?? await clock.TodayAsync(tenant.TenantId), IsBillable = request.IsBillable,
            Reference = Trim(request.Reference), Remarks = Trim(request.Remarks)
        };
        db.TripIncomes.Add(income);
        await db.SaveChangesAsync(ct);
        return ToModel(income);
    }

    public async Task<TripIncomeModel> VoidAsync(long tripIncomeId, VoidTripIncomeRequest request, int voidedByUserId, CancellationToken ct = default)
    {
        var income = await db.TripIncomes.FirstOrDefaultAsync(i => i.TenantId == tenant.TenantId && i.TripIncomeId == tripIncomeId, ct)
            ?? throw new NotFoundException($"Trip income {tripIncomeId} was not found.");
        if (income.IsVoided) throw new ConflictException("This income entry is already voided.");
        // §30: "a billed income row cannot be edited" — no edit action exists at all yet, so this is the one
        // place the guard is real today; always false until invoicing (CC-23+) ever sets InvoiceLineId.
        if (income.InvoiceLineId is not null) throw new ConflictException("This income is already on an invoice and cannot be voided.");
        if (string.IsNullOrWhiteSpace(request.Reason)) throw new ValidationException(messages.Error("reason", Msg.Required, ("Field", "Void reason")));

        income.IsVoided = true;
        income.VoidReason = request.Reason.Trim();
        income.VoidedBy = voidedByUserId;
        income.VoidedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return ToModel(income);
    }

    public async Task<IReadOnlyList<TripIncomeModel>> ListBillableUnbilledAsync(int customerId, CancellationToken ct = default)
    {
        var rows = await db.TripIncomes.AsNoTracking()
            .Where(i => i.TenantId == tenant.TenantId && i.CustomerId == customerId && i.IsBillable && !i.IsVoided && i.InvoiceLineId == null)
            .OrderBy(i => i.IncomeDate).ToListAsync(ct);
        return rows.Select(ToModel).ToList();
    }

    private async Task<Trip> FindTripAsync(long tripId, CancellationToken ct) =>
        await db.Trips.AsNoTracking().FirstOrDefaultAsync(t => t.TenantId == tenant.TenantId && t.TripId == tripId, ct)
        ?? throw new NotFoundException($"Trip {tripId} was not found.");

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static TripIncomeModel ToModel(TripIncome i) => new()
    {
        TripIncomeId = i.TripIncomeId, TripId = i.TripId, CustomerId = i.CustomerId, IncomeTypeId = i.IncomeTypeId, Amount = i.Amount,
        CurrencyCode = i.CurrencyCode, IncomeDate = i.IncomeDate, IsBillable = i.IsBillable, InvoiceLineId = i.InvoiceLineId, Reference = i.Reference,
        Remarks = i.Remarks, IsVoided = i.IsVoided, VoidReason = i.VoidReason, VoidedBy = i.VoidedBy, VoidedAtUtc = i.VoidedAtUtc
    };
}
