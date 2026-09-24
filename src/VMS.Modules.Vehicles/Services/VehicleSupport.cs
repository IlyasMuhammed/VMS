using Microsoft.EntityFrameworkCore;
using VMS.Modules.Vehicles.Data;
using VMS.Modules.Vehicles.Domain;
using VMS.Modules.Vehicles.Models;
using VMS.Shared.Authorization;
using VMS.Shared.Branches;
using VMS.Shared.Common;
using VMS.Shared.Exceptions;
using VMS.Shared.Messages;
using VMS.Shared.Partners;
using VMS.Shared.Time;

namespace VMS.Modules.Vehicles.Services;

/// <summary>Who is acting, and which restricted values they may see or set.</summary>
public sealed record VehicleCaller(int UserId, string? UserName, bool IsSuperAdmin, IReadOnlySet<string> Permissions)
{
    public bool Has(string permission) => IsSuperAdmin || Permissions.Contains(permission);
}

/// <summary>
/// What the vehicle services share: the database, the day it is for the company, the partners a vehicle points at, and the
/// lifecycle log. One instance per request, so every service in it sees the same unit of work.
/// </summary>
internal sealed class VehicleContext(VehicleDbContext db, ITenantContext tenantContext, IOperatingClock clock, IPartnerDirectory partners, IBranchDirectory branches, IMessageCatalogue messages)
{
    public VehicleDbContext Db => db;
    public IMessageCatalogue Messages => messages;
    public IPartnerDirectory Partners => partners;
    public IBranchDirectory Branches => branches;
    public Guid Tenant => tenantContext.TenantId;

    public Task<DateOnly> TodayAsync() => clock.TodayAsync(Tenant);

    /// <summary>The company's own vehicles only: a Super Admin's requests bypass the query filter, and must still see one tenant's data.</summary>
    public IQueryable<Vehicle> Own() => db.Vehicles.Where(v => v.TenantId == Tenant && !v.IsDeleted);

    public async Task<Vehicle> LoadAsync(int id) =>
        await Own().FirstOrDefaultAsync(v => v.VehicleId == id) ?? throw new NotFoundException("Vehicle not found.");

    public async Task<Vehicle> LoadReadOnlyAsync(int id) =>
        await Own().AsNoTracking().FirstOrDefaultAsync(v => v.VehicleId == id) ?? throw new NotFoundException("Vehicle not found.");

    /// <summary>The partners named, by id, for showing on a screen. A partner that cannot be found is left out.</summary>
    public async Task<IReadOnlyDictionary<int, PartnerRef>> RefsAsync(IEnumerable<int?> ids)
    {
        var found = await partners.FindManyAsync(ids.Where(i => i.HasValue).Select(i => i!.Value));
        return found.ToDictionary(p => p.Key, p => new PartnerRef { Id = p.Value.Id, BpCode = p.Value.BpCode, Name = p.Value.DisplayName });
    }

    public static PartnerRef? Ref(IReadOnlyDictionary<int, PartnerRef> refs, int? id) => id is { } i && refs.TryGetValue(i, out var r) ? r : null;

    /// <summary>A vehicle that can take actions of the fleet: assigned, charged, maintained. A Draft or a disposed vehicle cannot.</summary>
    public ValidationException? NotInFleet(Vehicle v, string action) =>
        VehicleStatuses.IsInFleet(v.Status) ? null : new ValidationException(messages.Error("status", Msg.VhWrongStatus, ("Status", v.Status), ("Action", action)));

    public void LogEvent(Vehicle v, string eventType, DateOnly effective, VehicleCaller caller, string? fromStatus = null, string? toStatus = null,
        string? fromCategory = null, string? toCategory = null, string? reason = null, string? reference = null, int? counterpartyId = null, decimal? amount = null) =>
        db.Lifecycle.Add(new VehicleLifecycleEntry
        {
            VehicleId = v.VehicleId, EventType = eventType, FromStatus = fromStatus, ToStatus = toStatus, FromCategory = fromCategory, ToCategory = toCategory,
            EffectiveDate = effective, Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(), Reference = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim(),
            CounterpartyId = counterpartyId, Amount = amount, UserId = caller.UserId, UserName = caller.UserName, OccurredOn = DateTime.UtcNow
        });

    /// <summary>The reason a change is being refused because the vehicle was changed by someone else.</summary>
    public async Task<Exception> StaleAsync(int vehicleId)
    {
        var last = await db.Set<VMS.Shared.Auditing.AuditEntry>().AsNoTracking()
            .Where(a => a.TenantId == Tenant && a.RootEntity == "Vehicle" && a.RootRecordId == vehicleId.ToString())
            .OrderByDescending(a => a.AuditEntryID).FirstOrDefaultAsync();
        return new ConcurrencyConflictException(messages.Text(Msg.ChangedByAnother, ("User", last?.UserName ?? "another user"),
            ("At", last is null ? "just now" : $"{last.OccurredAt:yyyy-MM-dd HH:mm} UTC")));
    }
}

/// <summary>Registration numbers compare without spaces or dashes (BR-VH-017).</summary>
public static class RegistrationNumber
{
    /// <summary>The number as stored for display: trimmed, in capitals.</summary>
    public static string Display(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant();

    /// <summary><c>LES-1234</c> and <c>les 1234</c> both become <c>LES1234</c>.</summary>
    public static string Key(string? value) => new(Display(value).Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray());
}
