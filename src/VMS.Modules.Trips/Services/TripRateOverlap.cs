namespace VMS.Modules.Trips.Services;

public readonly record struct ExistingRateRange(long TripRateId, DateOnly EffectiveFrom, DateOnly? EffectiveTo);

public enum OverlapOutcome { Clear, Rejected, AutoCloses }

public readonly record struct OverlapResult(OverlapOutcome Outcome, long? CloseRateId, DateOnly? NewCloseEffectiveTo);

/// <summary>
/// FSD §26's overlap rule as a pure function, testable without a database (public for exactly that reason, the
/// same as <c>VMS.Modules.Vehicles.Services.ChargeSchedule</c>): "reject when new.From ≤ existing.To AND
/// new.To ≥ existing.From, except for the automatic close of an open-ended rate" — a later rate that starts strictly
/// after an existing open-ended rate's own start date closes it instead of being rejected; every other overlap,
/// against a finite range or against an open-ended rate that starts on/after the new one, is rejected outright.
/// </summary>
public static class TripRateOverlap
{
    public static OverlapResult Check(DateOnly newFrom, DateOnly? newTo, IReadOnlyList<ExistingRateRange> existingActiveRates, long? excludingRateId = null)
    {
        var effectiveNewTo = newTo ?? DateOnly.MaxValue;

        foreach (var existing in existingActiveRates)
        {
            if (existing.TripRateId == excludingRateId) continue;
            var existingTo = existing.EffectiveTo ?? DateOnly.MaxValue;
            var overlaps = newFrom <= existingTo && effectiveNewTo >= existing.EffectiveFrom;
            if (!overlaps) continue;

            if (existing.EffectiveTo is null && newFrom > existing.EffectiveFrom)
                return new OverlapResult(OverlapOutcome.AutoCloses, existing.TripRateId, newFrom.AddDays(-1));

            return new OverlapResult(OverlapOutcome.Rejected, null, null);
        }

        return new OverlapResult(OverlapOutcome.Clear, null, null);
    }
}
