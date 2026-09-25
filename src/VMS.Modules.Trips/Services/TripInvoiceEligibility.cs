using VMS.Modules.Trips.Domain;
using VMS.Modules.Trips.Models;

namespace VMS.Modules.Trips.Services;

/// <summary>§32.1's own eligibility rules 3/5/6/7, as a pure, DB-free-testable lookup — the same "pure business
/// rule" exception this codebase already established (<c>TripLifecycle</c>, <c>TripRateOverlap</c>). Shared by
/// <see cref="InvoiceEligibilityService"/> (the search) and <see cref="InvoiceCreationService"/> (CC-25's own
/// re-validation inside the generation transaction), so the two can never quietly drift apart on what "eligible"
/// means. Rule 1 (customer match) and rule 4 (not already invoiced) are structural to each caller's own query,
/// not part of this shared function; rule 2 (period) is the caller's own candidate filter.</summary>
internal static class TripInvoiceEligibility
{
    public static IReadOnlyList<string> BlockingReasons(Trip trip, bool podRequired, string? latestPodStatus, bool multiCurrencyEnabled, string baseCurrencyCode)
    {
        var reasons = new List<string>();
        if (!trip.IsActive) reasons.Add(TripBlockingCodes.Inactive);
        if (trip.Status != TripStatuses.Completed) reasons.Add(TripBlockingCodes.NotCompleted);
        if (trip.RateMissing || trip.TripAmount is null or <= 0) reasons.Add(TripBlockingCodes.RateMissing);
        if (podRequired && latestPodStatus != PodStatuses.Approved) reasons.Add(TripBlockingCodes.PodMissing);
        if (multiCurrencyEnabled && !string.Equals(trip.CurrencyCode, baseCurrencyCode, StringComparison.OrdinalIgnoreCase)) reasons.Add(TripBlockingCodes.CurrencyMismatch);
        return reasons;
    }
}
