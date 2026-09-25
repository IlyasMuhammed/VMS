using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using VMS.Modules.Trips.Data;
using VMS.Modules.Trips.Domain;
using VMS.Modules.Trips.Models;
using VMS.Shared.Common;
using VMS.Shared.Exceptions;
using VMS.Shared.Messages;

namespace VMS.Modules.Trips.Services;

public interface ITripRateService
{
    Task<IReadOnlyList<TripRateModel>> ListAsync(long tripConfigurationId, bool includeInactive, CancellationToken ct = default);
    Task<TripRateModel> CreateAsync(long tripConfigurationId, SaveTripRateRequest request, CancellationToken ct = default);
    Task<TripRateModel> UpdateAsync(long rateId, UpdateTripRateRequest request, CancellationToken ct = default);
    Task InactivateAsync(long rateId, InactivateTripRateRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<TripRateModel>> SplitAsync(long tripConfigurationId, SplitTripRateRequest request, CancellationToken ct = default);
    Task<RateResolutionResult> ResolveAsync(int customerId, long tripConfigurationId, DateOnly date, CancellationToken ct = default);
}

internal sealed class TripRateService(TripsDbContext db, ITenantContext tenant, IMessageCatalogue messages) : ITripRateService
{
    public async Task<IReadOnlyList<TripRateModel>> ListAsync(long tripConfigurationId, bool includeInactive, CancellationToken ct = default)
    {
        var query = db.TripRates.AsNoTracking().Where(r => r.TenantId == tenant.TenantId && r.TripConfigurationId == tripConfigurationId);
        if (!includeInactive) query = query.Where(r => r.Status == ActiveInactiveStatuses.Active);
        return await query.OrderBy(r => r.EffectiveFrom).Select(r => ToModel(r)).ToListAsync(ct);
    }

    public async Task<TripRateModel> CreateAsync(long tripConfigurationId, SaveTripRateRequest request, CancellationToken ct = default)
    {
        var config = await db.TripConfigurations.AsNoTracking().FirstOrDefaultAsync(c => c.TenantId == tenant.TenantId && c.TripConfigurationId == tripConfigurationId, ct)
            ?? throw new NotFoundException($"Trip configuration {tripConfigurationId} was not found.");

        var errors = new List<ValidationError>();
        void Add(string field, string code, params (string Name, object? Value)[] values) => errors.Add(messages.Error(field, code, values));
        if (request.RateAmount <= 0) Add("rateAmount", Msg.Min, ("Field", "Rate amount"), ("Min", "0.01"));
        if (request.EffectiveTo is { } to && to < request.EffectiveFrom) Add("effectiveTo", Msg.Min, ("Field", "Effective to"), ("Min", request.EffectiveFrom.ToString("yyyy-MM-dd")));

        var currencyCode = await ResolveCurrencyAsync(config.CustomerId, request.CurrencyCode, Add, ct);
        if (errors.Count > 0) throw new ValidationException(errors);

        return await InSerializableTransactionAsync(async ct2 =>
        {
            var existing = await db.TripRates.Where(r => r.TenantId == tenant.TenantId && r.TripConfigurationId == tripConfigurationId && r.Status == ActiveInactiveStatuses.Active)
                .Select(r => new ExistingRateRange(r.TripRateId, r.EffectiveFrom, r.EffectiveTo)).ToListAsync(ct2);
            var outcome = TripRateOverlap.Check(request.EffectiveFrom, request.EffectiveTo, existing);
            if (outcome.Outcome == OverlapOutcome.Rejected)
                throw new ValidationException(messages.Error("effectiveFrom", Msg.TrpRateOverlap));

            if (outcome.Outcome == OverlapOutcome.AutoCloses)
            {
                var toClose = await db.TripRates.FirstAsync(r => r.TenantId == tenant.TenantId && r.TripRateId == outcome.CloseRateId, ct2);
                toClose.EffectiveTo = outcome.NewCloseEffectiveTo;
            }

            var rate = new TripRate
            {
                CustomerId = config.CustomerId, TripConfigurationId = tripConfigurationId, EffectiveFrom = request.EffectiveFrom,
                EffectiveTo = request.EffectiveTo, RateAmount = request.RateAmount, CurrencyCode = currencyCode,
                Status = ActiveInactiveStatuses.Active, Remarks = string.IsNullOrWhiteSpace(request.Remarks) ? null : request.Remarks.Trim()
            };
            db.TripRates.Add(rate);
            await db.SaveChangesAsync(ct2);
            return ToModel(rate);
        }, ct);
    }

    public async Task<TripRateModel> UpdateAsync(long rateId, UpdateTripRateRequest request, CancellationToken ct = default)
    {
        var rate = await db.TripRates.FirstOrDefaultAsync(r => r.TenantId == tenant.TenantId && r.TripRateId == rateId, ct)
            ?? throw new NotFoundException($"Trip rate {rateId} was not found.");

        var errors = new List<ValidationError>();
        void Add(string field, string code, params (string Name, object? Value)[] values) => errors.Add(messages.Error(field, code, values));
        var amount = request.RateAmount ?? rate.RateAmount;
        if (amount <= 0) Add("rateAmount", Msg.Min, ("Field", "Rate amount"), ("Min", "0.01"));
        var effectiveFrom = request.EffectiveFrom ?? rate.EffectiveFrom;
        var effectiveTo = request.EffectiveTo ?? rate.EffectiveTo;
        if (effectiveTo is { } to && to < effectiveFrom) Add("effectiveTo", Msg.Min, ("Field", "Effective to"), ("Min", effectiveFrom.ToString("yyyy-MM-dd")));
        if (string.IsNullOrWhiteSpace(request.RowVersion)) Add("rowVersion", Msg.Required, ("Field", "Row version"));
        if (errors.Count > 0) throw new ValidationException(errors);

        try { db.Entry(rate).Property(r => r.RowVersion).OriginalValue = Convert.FromBase64String(request.RowVersion); }
        catch (FormatException) { throw new ValidationException(messages.Error("rowVersion", Msg.Invalid, ("Field", "Row version"))); }

        return await InSerializableTransactionAsync(async ct2 =>
        {
            if (effectiveFrom != rate.EffectiveFrom || effectiveTo != rate.EffectiveTo)
            {
                var siblings = await db.TripRates.Where(r => r.TenantId == tenant.TenantId && r.TripConfigurationId == rate.TripConfigurationId && r.Status == ActiveInactiveStatuses.Active)
                    .Select(r => new ExistingRateRange(r.TripRateId, r.EffectiveFrom, r.EffectiveTo)).ToListAsync(ct2);
                var outcome = TripRateOverlap.Check(effectiveFrom, effectiveTo, siblings, excludingRateId: rateId);
                if (outcome.Outcome == OverlapOutcome.Rejected)
                    throw new ValidationException(messages.Error("effectiveFrom", Msg.TrpRateOverlap));
                if (outcome.Outcome == OverlapOutcome.AutoCloses)
                {
                    var toClose = await db.TripRates.FirstAsync(r => r.TenantId == tenant.TenantId && r.TripRateId == outcome.CloseRateId, ct2);
                    toClose.EffectiveTo = outcome.NewCloseEffectiveTo;
                }
            }

            // §26: "amount/date edits are allowed but show the count of trips that hold the old snapshot" — Trip
            // doesn't exist yet (a later CC task); nothing to count today, so nothing is shown. Remarks are always
            // freely editable regardless (explicit in the FSD, not gated on trip usage at all).
            rate.RateAmount = amount;
            rate.EffectiveFrom = effectiveFrom;
            rate.EffectiveTo = effectiveTo;
            rate.Remarks = string.IsNullOrWhiteSpace(request.Remarks) ? rate.Remarks : request.Remarks.Trim();
            try { await db.SaveChangesAsync(ct2); }
            catch (DbUpdateConcurrencyException) { throw await StaleAsync(rateId, ct2); }
            return ToModel(rate);
        }, ct);
    }

    public async Task InactivateAsync(long rateId, InactivateTripRateRequest request, CancellationToken ct = default)
    {
        var rate = await db.TripRates.FirstOrDefaultAsync(r => r.TenantId == tenant.TenantId && r.TripRateId == rateId, ct)
            ?? throw new NotFoundException($"Trip rate {rateId} was not found.");
        if (rate.Status == ActiveInactiveStatuses.Inactive) throw new ConflictException("This rate is already Inactive.");
        if (string.IsNullOrWhiteSpace(request.RowVersion)) throw new ValidationException(messages.Error("rowVersion", Msg.Required, ("Field", "Row version")));

        try { db.Entry(rate).Property(r => r.RowVersion).OriginalValue = Convert.FromBase64String(request.RowVersion); }
        catch (FormatException) { throw new ValidationException(messages.Error("rowVersion", Msg.Invalid, ("Field", "Row version"))); }

        // §26: "Inactive rates are ignored and free their range" — no other row is touched; a later save can reuse
        // the space this one occupied.
        rate.Status = ActiveInactiveStatuses.Inactive;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw await StaleAsync(rateId, ct); }
    }

    public async Task<IReadOnlyList<TripRateModel>> SplitAsync(long tripConfigurationId, SplitTripRateRequest request, CancellationToken ct = default)
    {
        if (!await db.TripConfigurations.AnyAsync(c => c.TenantId == tenant.TenantId && c.TripConfigurationId == tripConfigurationId, ct))
            throw new NotFoundException($"Trip configuration {tripConfigurationId} was not found.");
        if (request.RateAmount <= 0) throw new ValidationException(messages.Error("rateAmount", Msg.Min, ("Field", "Rate amount"), ("Min", "0.01")));

        return await InSerializableTransactionAsync(async ct2 =>
        {
            var covering = await db.TripRates.FirstOrDefaultAsync(r => r.TenantId == tenant.TenantId && r.TripConfigurationId == tripConfigurationId
                && r.Status == ActiveInactiveStatuses.Active && r.EffectiveFrom <= request.Date && (r.EffectiveTo == null || r.EffectiveTo >= request.Date), ct2)
                ?? throw new ValidationException(messages.Error("date", Msg.Invalid, ("Field", "No active rate covers this date to split")));

            var originalTo = covering.EffectiveTo;
            var results = new List<TripRate>();

            // §26's own worked example: 01-10 Jul / 11 Jul / 12-31 Jul, in one transaction, each row audited.
            covering.EffectiveTo = request.Date.AddDays(-1);
            results.Add(covering);

            var middle = new TripRate
            {
                CustomerId = covering.CustomerId, TripConfigurationId = tripConfigurationId, EffectiveFrom = request.Date, EffectiveTo = request.Date,
                RateAmount = request.RateAmount, CurrencyCode = covering.CurrencyCode, Status = ActiveInactiveStatuses.Active,
                Remarks = string.IsNullOrWhiteSpace(request.Remarks) ? null : request.Remarks.Trim()
            };
            db.TripRates.Add(middle);
            results.Add(middle);

            if (originalTo is null || originalTo > request.Date)
            {
                var after = new TripRate
                {
                    CustomerId = covering.CustomerId, TripConfigurationId = tripConfigurationId, EffectiveFrom = request.Date.AddDays(1), EffectiveTo = originalTo,
                    RateAmount = covering.RateAmount, CurrencyCode = covering.CurrencyCode, Status = ActiveInactiveStatuses.Active
                };
                db.TripRates.Add(after);
                results.Add(after);
            }

            await db.SaveChangesAsync(ct2);
            return (IReadOnlyList<TripRateModel>)results.Select(r => ToModel(r)).ToList();
        }, ct);
    }

    public async Task<RateResolutionResult> ResolveAsync(int customerId, long tripConfigurationId, DateOnly date, CancellationToken ct = default)
    {
        var rate = await db.TripRates.AsNoTracking().FirstOrDefaultAsync(r => r.TenantId == tenant.TenantId && r.CustomerId == customerId
            && r.TripConfigurationId == tripConfigurationId && r.Status == ActiveInactiveStatuses.Active
            && r.EffectiveFrom <= date && (r.EffectiveTo == null || r.EffectiveTo >= date), ct);

        // §26: "Never fall back to previous, next, average or zero rate." Found = false is the whole answer.
        return rate is null
            ? new RateResolutionResult { Found = false }
            : new RateResolutionResult { Found = true, TripRateId = rate.TripRateId, RateAmount = rate.RateAmount, CurrencyCode = rate.CurrencyCode, EffectiveFrom = rate.EffectiveFrom, EffectiveTo = rate.EffectiveTo };
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────

    private async Task<string> ResolveCurrencyAsync(int customerId, string? requested, Action<string, string, (string, object?)[]> add, CancellationToken ct)
    {
        var settings = await db.CurrencySettings.AsNoTracking().FirstAsync(s => s.TenantId == tenant.TenantId, ct);
        if (!settings.MultiCurrencyEnabled) return settings.BaseCurrencyCode;

        var customer = await db.Customers.AsNoTracking().FirstAsync(c => c.TenantId == tenant.TenantId && c.CustomerId == customerId, ct);
        var code = string.IsNullOrWhiteSpace(requested) ? customer.CurrencyCode : requested.Trim().ToUpperInvariant();
        if (!await db.Currencies.AnyAsync(c => c.TenantId == tenant.TenantId && c.CurrencyCode == code, ct))
            add("currencyCode", Msg.TrpExchangeRateCurrencyUnknown, [("Code", code)]);
        return code;
    }

    /// <summary>§26: "Enforced in the service layer and by a DB check inside a serializable transaction (SQL Server
    /// has no exclusion constraint)" — the isolation level is what makes two concurrent inserts unable to both read
    /// "no overlap" and both commit; one blocks or is chosen as a deadlock victim, and either way loses the race
    /// honestly (a 409 telling the caller to retry) rather than silently creating an overlap.</summary>
    private async Task<T> InSerializableTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is not null) return await work(ct);

        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            try
            {
                var result = await work(ct);
                await transaction.CommitAsync(ct);
                return result;
            }
            catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 1205 })
            {
                throw new ConcurrencyConflictException("Another request is saving a rate for this configuration at the same time. Please try again.");
            }
        });
    }

    private async Task<Exception> StaleAsync(long rateId, CancellationToken ct)
    {
        var last = await db.Set<VMS.Shared.Auditing.AuditEntry>().AsNoTracking()
            .Where(a => a.TenantId == tenant.TenantId && a.RootEntity == "TripConfiguration")
            .OrderByDescending(a => a.AuditEntryID).FirstOrDefaultAsync(ct);
        return new ConcurrencyConflictException(messages.Text(Msg.ChangedByAnother, ("User", last?.UserName ?? "another user"),
            ("At", last is null ? "just now" : $"{last.OccurredAt:yyyy-MM-dd HH:mm} UTC")));
    }

    private static TripRateModel ToModel(TripRate r) => new()
    {
        TripRateId = r.TripRateId, CustomerId = r.CustomerId, TripConfigurationId = r.TripConfigurationId, EffectiveFrom = r.EffectiveFrom,
        EffectiveTo = r.EffectiveTo, RateAmount = r.RateAmount, CurrencyCode = r.CurrencyCode, Status = r.Status, Remarks = r.Remarks,
        RowVersion = Convert.ToBase64String(r.RowVersion)
    };
}
