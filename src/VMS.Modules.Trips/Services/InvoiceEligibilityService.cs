using Microsoft.EntityFrameworkCore;
using VMS.Modules.Trips.Data;
using VMS.Modules.Trips.Domain;
using VMS.Modules.Trips.Models;
using VMS.Shared.Common;
using VMS.Shared.Exceptions;
using VMS.Shared.Messages;
using VMS.Shared.Time;
using VMS.Shared.Vehicles;

namespace VMS.Modules.Trips.Services;

/// <summary>Eligible-trip search and overlap check (§32.1, §39, §47.3, AC-26, AC-57). Read-only — nothing here
/// writes anything; invoice creation itself is CC-25's own job, which re-runs this same eligibility inside its
/// own transaction rather than trusting a stale search result (§47.3: "Server steps ... revalidate").</summary>
public interface IInvoiceEligibilityService
{
    Task<EligibleTripsResult> SearchAsync(EligibleTripsQuery query, CancellationToken ct = default);
}

internal sealed class InvoiceEligibilityService(
    TripsDbContext db, ITenantContext tenant, IMessageCatalogue messages, IOperatingClock clock, IVehicleDirectory vehicles,
    ICustomerBillingConfigurationService billingConfigurations, ICustomerInvoiceTemplateService templates,
    ICustomerBillingAddressService billingAddresses, ICustomerTaxRuleService taxRules, ICurrencyService currency) : IInvoiceEligibilityService
{
    public async Task<EligibleTripsResult> SearchAsync(EligibleTripsQuery query, CancellationToken ct = default)
    {
        var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.TenantId == tenant.TenantId && c.CustomerId == query.CustomerId, ct);
        if (customer is null) throw new ValidationException(messages.Error("customerId", Msg.Invalid, ("Field", "Customer")));
        if (customer.Status != CustomerStatuses.Active) throw new ValidationException(messages.Error("customerId", Msg.Invalid, ("Field", "Customer (not Active)")));
        if (query.PeriodFrom > query.PeriodTo) throw new ValidationException(messages.Error("periodTo", Msg.Invalid, ("Field", "Period to (must be on or after Period from)")));
        // §32: "max span 366 days (Recommended Design)."
        if (query.PeriodTo.DayNumber - query.PeriodFrom.DayNumber > 366)
            throw new ValidationException(messages.Error("periodTo", Msg.Invalid, ("Field", "Period (spans more than 366 days)")));

        var invoiceDate = query.InvoiceDate ?? await clock.TodayAsync(tenant.TenantId);
        var billing = await billingConfigurations.GetCurrentAsync(query.CustomerId, ct);
        var settings = await currency.GetSettingsAsync(ct);

        // §32.1: the candidate pool is every trip for the customer whose CompletionDate falls in the period
        // (rule 2) — a trip that has never completed has no CompletionDate and is never part of this search at all.
        var candidates = await db.Trips.AsNoTracking()
            .Where(t => t.TenantId == tenant.TenantId && t.CustomerId == query.CustomerId
                && t.CompletionDate != null && t.CompletionDate >= query.PeriodFrom && t.CompletionDate <= query.PeriodTo)
            .ToListAsync(ct);

        var activeLinksByTrip = await db.InvoiceTripLinks.AsNoTracking()
            .Where(l => l.TenantId == tenant.TenantId && l.IsActive && candidates.Select(c => c.TripId).Contains(l.TripId))
            .ToDictionaryAsync(l => l.TripId, ct);

        var configIds = candidates.Where(t => t.TripConfigurationId is not null).Select(t => t.TripConfigurationId!.Value).Distinct().ToList();
        var configs = configIds.Count == 0 ? new Dictionary<long, TripConfiguration>() : await db.TripConfigurations.AsNoTracking()
            .Where(c => c.TenantId == tenant.TenantId && configIds.Contains(c.TripConfigurationId)).ToDictionaryAsync(c => c.TripConfigurationId, ct);

        var vehicleIds = candidates.Select(t => t.VehicleId).Distinct().ToList();
        var vehicleInfos = await vehicles.FindManyAsync(vehicleIds, ct);

        var podStatusByTrip = billing.PodRequired
            ? await LatestPodStatusByTripAsync(candidates.Select(c => c.TripId), ct)
            : new Dictionary<long, string?>();

        var rows = new List<EligibleTripRow>();
        var blockingErrors = new List<TripBlockingError>();
        int alreadyInvoiced = 0, blocked = 0;

        foreach (var trip in candidates)
        {
            var route = await RouteLabels.BuildAsync(db, tenant.TenantId, trip, ct);
            var vehicleLabel = vehicleInfos.TryGetValue(trip.VehicleId, out var vehicleInfo) ? vehicleInfo.RegistrationNo : null;

            // Rule 4: not linked to any invoice that is active — except the one being regenerated, treated as
            // "available for that regeneration only" (§32.1).
            if (activeLinksByTrip.TryGetValue(trip.TripId, out var link) && link.InvoiceId != query.RegeneratingInvoiceId)
            {
                alreadyInvoiced++;
                rows.Add(ToRow(trip, EligibleTripCategories.AlreadyInvoiced, vehicleLabel, route));
                continue;
            }

            var reasons = TripInvoiceEligibility.BlockingReasons(trip, billing.PodRequired, podStatusByTrip.GetValueOrDefault(trip.TripId), settings.MultiCurrencyEnabled, settings.BaseCurrencyCode);

            if (reasons.Count > 0)
            {
                blocked++;
                rows.Add(ToRow(trip, EligibleTripCategories.Blocked, vehicleLabel, route));
                var configCode = trip.TripConfigurationId is { } cid && configs.TryGetValue(cid, out var config) ? config.TripCode : null;
                foreach (var reason in reasons) blockingErrors.Add(new TripBlockingError { Code = reason, TripId = trip.TripId, TripDate = trip.TripDate, Configuration = configCode });
                continue;
            }

            rows.Add(ToRow(trip, EligibleTripCategories.Available, vehicleLabel, route));
        }

        var overlaps = await db.Invoices.AsNoTracking()
            .Where(i => i.TenantId == tenant.TenantId && i.CustomerId == query.CustomerId && i.IsActive
                && (query.RegeneratingInvoiceId == null || i.InvoiceId != query.RegeneratingInvoiceId)
                && i.PeriodFrom <= query.PeriodTo && i.PeriodTo >= query.PeriodFrom)
            .Select(i => new InvoiceOverlapRow
            {
                InvoiceId = i.InvoiceId, InvoiceNumber = i.InvoiceNumber, PeriodFrom = i.PeriodFrom, PeriodTo = i.PeriodTo,
                PaymentStatus = i.PaymentStatus, FullyPaid = i.PaymentStatus == InvoicePaymentStatuses.Paid
            }).ToListAsync(ct);

        var available = rows.Where(r => r.Category == EligibleTripCategories.Available).ToList();
        var resolved = new ResolvedInvoiceOptions
        {
            TemplateOptions = (await templates.ResolveApplicableAsync(query.CustomerId, invoiceDate, ct)).Templates
                .Select(t => new InvoiceTemplateOption { Id = t.CustomerInvoiceTemplateId, Name = t.TemplateName, Version = t.Version, IsDefault = t.IsDefault }).ToList(),
            BillingAddressOptions = (await billingAddresses.ListAsync(query.CustomerId, includeInactive: false, ct))
                .Select(a => new InvoiceAddressOption { Id = a.CustomerBillingAddressId, Name = a.AddressName, IsDefault = a.IsDefault }).ToList(),
            TaxRulesPreview = (await taxRules.ResolveApplicableAsync(query.CustomerId, invoiceDate, ct))
                .Select(r => new TaxRulePreview { TaxCode = r.TaxCode, Rate = r.TaxType == TaxTypes.Percentage ? r.TaxPercentage : r.FixedAmount, CalculationBasis = r.CalculationBasis }).ToList()
        };

        return new EligibleTripsResult
        {
            Summary = new EligibleTripsSummary
            {
                CompletedTrips = candidates.Count, AlreadyInvoiced = alreadyInvoiced, Blocked = blocked,
                Available = available.Count, AvailableAmount = available.Sum(r => r.Amount ?? 0)
            },
            Trips = rows, BlockingErrors = blockingErrors, Overlaps = overlaps, Resolved = resolved
        };
    }

    private static EligibleTripRow ToRow(Trip trip, string category, string? vehicle, string? route) => new()
    {
        TripId = trip.TripId, TripNumber = trip.TripNumber, TripDate = trip.TripDate, Category = category, Amount = trip.TripAmount,
        Vehicle = vehicle, Route = route, CustomerTripReference = trip.CustomerTripReference, RowVersion = Convert.ToBase64String(trip.RowVersion)
    };

    private async Task<Dictionary<long, string?>> LatestPodStatusByTripAsync(IEnumerable<long> tripIds, CancellationToken ct)
    {
        var ids = tripIds.ToList();
        if (ids.Count == 0) return new Dictionary<long, string?>();
        var pods = await db.TripPODs.AsNoTracking().Where(p => p.TenantId == tenant.TenantId && ids.Contains(p.TripId)).ToListAsync(ct);
        return pods.GroupBy(p => p.TripId).ToDictionary(g => g.Key, g => (string?)g.OrderByDescending(p => p.UploadedAtUtc).First().Status);
    }

}
