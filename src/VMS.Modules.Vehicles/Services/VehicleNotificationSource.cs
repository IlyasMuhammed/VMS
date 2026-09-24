using Microsoft.EntityFrameworkCore;
using VMS.Modules.Vehicles.Data;
using VMS.Modules.Vehicles.Domain;
using VMS.Shared.Common;
using VMS.Shared.Lookups;
using VMS.Shared.Notifications;

namespace VMS.Modules.Vehicles.Services;

/// <summary>
/// Answers the Notifications module's "what is due" question for a vehicle's charges, installments and attached items
/// (FSD §19A.6, §23.4): every recurring charge entry that is Due or Overdue, every active charge whose own schedule is
/// ending, every pending bank installment, every attached item nearing the end of its warranty. "Rent due day (Rented)"
/// is not a source of its own — Vehicle Rent Payable is itself a recurring charge (Stage 4), so it already comes through
/// as a <see cref="NotificationEventTypes.ChargeDue"/> candidate like any other charge type.
/// </summary>
internal sealed class VehicleNotificationSource(VehicleDbContext db, ITenantContext tenantContext, ILookupReader lookups) : IVehicleNotificationSource
{
    public async Task<IReadOnlyList<NotificationCandidate>> FindDueAsync(CancellationToken ct = default)
    {
        var tenant = tenantContext.TenantId;

        var dueEntries = await db.RecurringChargeEntries.AsNoTracking()
            .Where(e => e.TenantId == tenant && (e.Status == ChargeEntryStatuses.Due || e.Status == ChargeEntryStatuses.Overdue))
            .ToListAsync(ct);
        var endingCharges = await db.RecurringCharges.AsNoTracking()
            .Where(c => c.TenantId == tenant && c.EffectiveTo == null && c.EndDate != null)
            .ToListAsync(ct);
        var installments = await db.Installments.AsNoTracking()
            .Where(i => i.TenantId == tenant && i.Status == InstallmentStatuses.Pending)
            .ToListAsync(ct);
        var items = await db.Items.AsNoTracking()
            .Where(i => i.TenantId == tenant && i.Status == ItemStatuses.Attached && i.WarrantyUntil != null)
            .ToListAsync(ct);

        if (dueEntries.Count == 0 && endingCharges.Count == 0 && installments.Count == 0 && items.Count == 0) return [];

        var vehicleIds = dueEntries.Select(e => e.VehicleId)
            .Concat(endingCharges.Select(c => c.VehicleId))
            .Concat(installments.Select(i => i.VehicleId))
            .Concat(items.Select(i => i.VehicleId))
            .Distinct().ToList();
        var vehicles = await db.Vehicles.AsNoTracking().Where(v => vehicleIds.Contains(v.VehicleId)).ToDictionaryAsync(v => v.VehicleId, v => v.RegistrationNo, ct);

        var chargeIds = dueEntries.Select(e => e.VehicleRecurringChargeId).Concat(endingCharges.Select(c => c.VehicleRecurringChargeId)).Distinct().ToList();
        var chargesById = chargeIds.Count > 0 ? await db.RecurringCharges.AsNoTracking().Where(c => chargeIds.Contains(c.VehicleRecurringChargeId)).ToDictionaryAsync(c => c.VehicleRecurringChargeId, ct) : [];
        var chargeTypeIds = dueEntries.Select(e => chargesById.GetValueOrDefault(e.VehicleRecurringChargeId)?.ChargeTypeId ?? 0)
            .Concat(endingCharges.Select(c => c.ChargeTypeId)).Where(id => id > 0).Distinct();
        var chargeTypes = await lookups.FindManyAsync(PlatformLookups.RecurringChargeType, chargeTypeIds);

        var result = new List<NotificationCandidate>();

        foreach (var e in dueEntries)
        {
            var reg = vehicles.GetValueOrDefault(e.VehicleId, $"#{e.VehicleId}");
            var charge = chargesById.GetValueOrDefault(e.VehicleRecurringChargeId);
            var typeName = charge is not null ? chargeTypes.GetValueOrDefault(charge.ChargeTypeId)?.Description ?? "Charge" : "Charge";
            var eventType = e.Status == ChargeEntryStatuses.Overdue ? NotificationEventTypes.ChargeOverdue : NotificationEventTypes.ChargeDue;
            result.Add(new NotificationCandidate(eventType, nameof(VehicleRecurringChargeEntry), e.VehicleRecurringChargeEntryId.ToString(),
                "Vehicle", e.VehicleId, reg, e.DueDate, $"{typeName} of {e.ExpectedAmount:N0} for {reg} is {(e.Status == ChargeEntryStatuses.Overdue ? "overdue since" : "due on")} {e.DueDate:yyyy-MM-dd}."));
        }

        foreach (var c in endingCharges)
        {
            var reg = vehicles.GetValueOrDefault(c.VehicleId, $"#{c.VehicleId}");
            var typeName = chargeTypes.GetValueOrDefault(c.ChargeTypeId)?.Description ?? "Charge";
            result.Add(new NotificationCandidate(NotificationEventTypes.ChargeEnding, nameof(VehicleRecurringCharge), c.VehicleRecurringChargeId.ToString(),
                "Vehicle", c.VehicleId, reg, c.EndDate!.Value, $"{typeName} schedule for {reg} ends on {c.EndDate:yyyy-MM-dd}."));
        }

        foreach (var i in installments)
        {
            var reg = vehicles.GetValueOrDefault(i.VehicleId, $"#{i.VehicleId}");
            result.Add(new NotificationCandidate(NotificationEventTypes.InstallmentDue, nameof(VehicleInstallment), i.VehicleInstallmentId.ToString(),
                "Vehicle", i.VehicleId, reg, i.DueDate, $"Installment #{i.InstallmentNo} of {i.ExpectedAmount:N0} for {reg} is due on {i.DueDate:yyyy-MM-dd}."));
        }

        foreach (var i in items)
        {
            var reg = vehicles.GetValueOrDefault(i.VehicleId, $"#{i.VehicleId}");
            result.Add(new NotificationCandidate(NotificationEventTypes.ItemWarrantyEnd, nameof(VehicleAttachedItem), i.VehicleAttachedItemId.ToString(),
                "Vehicle", i.VehicleId, reg, i.WarrantyUntil!.Value, $"Warranty for {i.Description} on {reg} ends on {i.WarrantyUntil:yyyy-MM-dd}."));
        }

        return result;
    }
}
