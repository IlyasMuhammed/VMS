using Microsoft.EntityFrameworkCore;
using VMS.Modules.Trips.Data;
using VMS.Modules.Trips.Domain;
using VMS.Modules.Trips.Models;
using VMS.Shared.Common;
using VMS.Shared.Exceptions;
using VMS.Shared.Messages;

namespace VMS.Modules.Trips.Services;

/// <summary>§26: "When is a trip rate re-resolved? Only by explicit action" — this task's own two of the three
/// (the third, "automatically when a trip's TripDate, configuration or customer is changed before invoicing," has
/// no Trip Edit endpoint to hang off yet — CC-13's own note already deferred that to the invoicing tasks, CC-23+;
/// nothing here pretends otherwise).</summary>
public interface ITripRepricingService
{
    Task<ResolveMissingRatesResult> ResolveMissingRatesAsync(ResolveMissingRatesRequest request, int performedByUserId, CancellationToken ct = default);
    Task<RepriceResult> RepriceAsync(RepriceTripsRequest request, int performedByUserId, CancellationToken ct = default);
}

internal sealed class TripRepricingService(TripsDbContext db, ITenantContext tenant, ITripRateService rates, IMessageCatalogue messages) : ITripRepricingService
{
    public async Task<ResolveMissingRatesResult> ResolveMissingRatesAsync(ResolveMissingRatesRequest request, int performedByUserId, CancellationToken ct = default)
    {
        var query = db.Trips.Where(t => t.TenantId == tenant.TenantId && t.RateMissing && t.InvoiceId == null);
        if (request.CustomerId is { } customerId) query = query.Where(t => t.CustomerId == customerId);
        if (request.TripConfigurationId is { } configId) query = query.Where(t => t.TripConfigurationId == configId);
        var trips = await query.ToListAsync(ct);

        var result = new ResolveMissingRatesResult { Considered = trips.Count };
        var now = DateTime.UtcNow;
        foreach (var trip in trips)
        {
            // RateMissing only ever occurs on a Fixed trip (Open trips are always Manual/never Missing, §22) — but
            // the null-forgiving TripConfigurationId is guarded anyway rather than assumed.
            if (trip.TripConfigurationId is not { } configurationId) continue;
            var resolution = await rates.ResolveAsync(trip.CustomerId, configurationId, trip.TripDate, ct);
            if (!resolution.Found) continue;

            db.TripRateHistories.Add(new TripRateHistory
            {
                TripId = trip.TripId, OldTripRateId = null, OldRateAmount = null, OldRateSource = TripRateSources.Missing, OldCurrencyCode = null,
                NewTripRateId = resolution.TripRateId, NewRateAmount = resolution.RateAmount, NewRateSource = TripRateSources.Configured, NewCurrencyCode = resolution.CurrencyCode,
                Action = TripRateHistoryActions.ResolveMissingRates, PerformedBy = performedByUserId, PerformedAtUtc = now
            });

            trip.TripRateId = resolution.TripRateId;
            trip.TripRateAmount = resolution.RateAmount;
            trip.RateEffectiveFrom = resolution.EffectiveFrom;
            trip.RateEffectiveTo = resolution.EffectiveTo;
            trip.RateSource = TripRateSources.Configured;
            trip.TripAmount = resolution.RateAmount;
            trip.CurrencyCode = resolution.CurrencyCode;
            trip.RateMissing = false;
            result.Updated++;
            result.UpdatedTripIds.Add(trip.TripId);
        }
        result.StillMissing = result.Considered - result.Updated;
        if (result.Updated > 0) await db.SaveChangesAsync(ct);
        return result;
    }

    public async Task<RepriceResult> RepriceAsync(RepriceTripsRequest request, int performedByUserId, CancellationToken ct = default)
    {
        if (request.Commit && string.IsNullOrWhiteSpace(request.Reason))
            throw new ValidationException(messages.Error("reason", Msg.Required, ("Field", "Reason")));
        if (request.TripIds.Count == 0)
            throw new ValidationException(messages.Error("tripIds", Msg.Required, ("Field", "Trips")));

        var trips = await db.Trips.Where(t => t.TenantId == tenant.TenantId && request.TripIds.Contains(t.TripId)).ToListAsync(ct);
        var found = trips.ToDictionary(t => t.TripId);
        var result = new RepriceResult();
        var now = DateTime.UtcNow;

        foreach (var tripId in request.TripIds)
        {
            if (!found.TryGetValue(tripId, out var trip))
                throw new NotFoundException($"Trip {tripId} was not found.");

            // §26: "Trips on an active invoice are excluded; they change only through invoice regeneration."
            if (trip.InvoiceId is not null)
            {
                result.Items.Add(new RepriceResultItem
                {
                    TripId = trip.TripId, TripNumber = trip.TripNumber, OldAmount = trip.TripAmount, OldRateSource = trip.RateSource,
                    NewAmount = trip.TripAmount, NewRateSource = trip.RateSource, Excluded = true, ExcludedReason = "On an active invoice; use invoice regeneration instead."
                });
                continue;
            }
            // Open trips carry a manual amount, not a rate-master snapshot — nothing to re-resolve against.
            if (trip.TripConfigurationId is not { } configurationId)
            {
                result.Items.Add(new RepriceResultItem
                {
                    TripId = trip.TripId, TripNumber = trip.TripNumber, OldAmount = trip.TripAmount, OldRateSource = trip.RateSource,
                    NewAmount = trip.TripAmount, NewRateSource = trip.RateSource, Excluded = true, ExcludedReason = "Open trips have a manual amount; re-pricing does not apply."
                });
                continue;
            }

            var resolution = await rates.ResolveAsync(trip.CustomerId, configurationId, trip.TripDate, ct);
            var item = new RepriceResultItem
            {
                TripId = trip.TripId, TripNumber = trip.TripNumber, OldAmount = trip.TripAmount, OldRateSource = trip.RateSource,
                NewAmount = resolution.Found ? resolution.RateAmount : null, NewRateSource = resolution.Found ? TripRateSources.Configured : TripRateSources.Missing
            };
            result.Items.Add(item);

            if (request.Commit)
            {
                db.TripRateHistories.Add(new TripRateHistory
                {
                    TripId = trip.TripId, OldTripRateId = trip.TripRateId, OldRateAmount = trip.TripAmount, OldRateSource = trip.RateSource, OldCurrencyCode = trip.CurrencyCode,
                    NewTripRateId = resolution.TripRateId, NewRateAmount = item.NewAmount, NewRateSource = item.NewRateSource, NewCurrencyCode = resolution.CurrencyCode,
                    Action = TripRateHistoryActions.Reprice, Reason = request.Reason!.Trim(), PerformedBy = performedByUserId, PerformedAtUtc = now
                });

                if (resolution.Found)
                {
                    trip.TripRateId = resolution.TripRateId;
                    trip.TripRateAmount = resolution.RateAmount;
                    trip.RateEffectiveFrom = resolution.EffectiveFrom;
                    trip.RateEffectiveTo = resolution.EffectiveTo;
                    trip.RateSource = TripRateSources.Configured;
                    trip.TripAmount = resolution.RateAmount;
                    trip.CurrencyCode = resolution.CurrencyCode;
                    trip.RateMissing = false;
                }
                else
                {
                    // §26: "Never fall back to previous, next, average or zero rate" — a re-price can honestly turn
                    // a previously-rated trip into RateMissing if the master no longer covers its date.
                    trip.TripRateId = null;
                    trip.TripRateAmount = null;
                    trip.RateEffectiveFrom = null;
                    trip.RateEffectiveTo = null;
                    trip.RateSource = TripRateSources.Missing;
                    trip.TripAmount = null;
                    trip.RateMissing = true;
                }
            }
        }

        if (request.Commit)
        {
            result.Committed = true;
            await db.SaveChangesAsync(ct);
        }
        return result;
    }
}
