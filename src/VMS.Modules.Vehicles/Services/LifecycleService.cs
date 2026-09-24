using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using VMS.Modules.Vehicles.Domain;
using VMS.Modules.Vehicles.Models;
using VMS.Shared.Auditing;
using VMS.Shared.Authorization;
using VMS.Shared.Exceptions;
using VMS.Shared.Messages;
using VMS.Shared.Numbering;
using VMS.Shared.Pagination;

namespace VMS.Modules.Vehicles.Services;

public interface ILifecycleService
{
    Task<VehicleModel> ChangeStatusAsync(int id, ChangeStatusRequest request, VehicleCaller caller);
    Task<VehicleModel> DisposeAsync(int id, DisposeRequest request, VehicleCaller caller);
    Task<VehicleHistory> HistoryAsync(int id, int page, int pageSize, ClaimsPrincipal user);
}

/// <summary>The status of a vehicle and how it leaves the fleet (FSD §23.1, BR-VH-023). Every move writes a lifecycle row: from, to, when, why, who.</summary>
internal sealed class LifecycleService(VehicleContext ctx, IVehicleService vehicles, IRecurringChargeService recurringCharges) : ILifecycleService
{
    private const int MaxPageSize = 100;

    // ── Status ──────────────────────────────────────────────────────────────────────

    public async Task<VehicleModel> ChangeStatusAsync(int id, ChangeStatusRequest request, VehicleCaller caller)
    {
        var vehicle = await ctx.LoadAsync(id);
        var to = request.Status?.Trim() ?? string.Empty;
        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        var today = await ctx.TodayAsync();
        var effective = request.EffectiveDate ?? today;

        var errors = new List<ValidationError>();
        if (!VehicleStatuses.Settable.Contains(to)) errors.Add(ctx.Messages.Error("status", Msg.OneOf, ("Field", "Status"), ("Allowed", string.Join(", ", VehicleStatuses.Settable))));
        if (effective > today) errors.Add(ctx.Messages.Error("effectiveDate", Msg.NotFuture, ("Field", "Effective date")));
        if (reason is { Length: > 500 }) errors.Add(ctx.Messages.Error("reason", Msg.MaxLength, ("Field", "Reason"), ("Max", 500)));
        if (errors.Count > 0) throw new ValidationException(errors);

        // A Draft is activated, not moved (FR-VH-001); a sold or transferred vehicle has left. Retired may be reinstated as Active.
        var canMove = VehicleStatuses.IsInFleet(vehicle.Status) || (vehicle.Status == VehicleStatuses.Retired && to == VehicleStatuses.Active);
        if (!canMove) throw new ValidationException(ctx.Messages.Error("status", Msg.VhWrongStatus, ("Status", vehicle.Status), ("Action", "moved to " + to)));
        if (vehicle.Status == to) throw new ConflictException($"The vehicle is already {to}.");

        var from = vehicle.Status;
        await ctx.Db.InTransactionAsync(async ct =>
        {
            vehicle.Status = to;
            vehicle.ModifiedBy = caller.UserId;
            vehicle.ModifiedOn = DateTime.UtcNow;
            ctx.LogEvent(vehicle, LifecycleEvents.StatusChange, effective, caller, fromStatus: from, toStatus: to, reason: reason);
            await ctx.Db.SaveChangesAsync(ct);
        });
        return await vehicles.GetAsync(id);
    }

    // ── Leaving the fleet ───────────────────────────────────────────────────────────

    public async Task<VehicleModel> DisposeAsync(int id, DisposeRequest request, VehicleCaller caller)
    {
        var vehicle = await ctx.LoadAsync(id);
        if (ctx.NotInFleet(vehicle, "retired, sold or transferred") is { } wrong && vehicle.Status != VehicleStatuses.Retired) throw wrong;

        var kind = request.Kind?.Trim() ?? string.Empty;
        var today = await ctx.TodayAsync();
        var date = request.Date ?? today;
        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();

        var errors = new List<ValidationError>();
        void Add(string field, string code, params (string Name, object? Value)[] values) => errors.Add(ctx.Messages.Error(field, code, values));

        if (!DisposalKinds.All.Contains(kind)) Add("kind", Msg.OneOf, ("Field", "Action"), ("Allowed", string.Join(", ", DisposalKinds.All)));
        if (date > today) Add("date", Msg.NotFuture, ("Field", "Date"));
        if (vehicle.AcquisitionDate is { } acquired && date < acquired) Add("date", Msg.VhItemBeforeAcquisition);
        if (reason is null) Add("reason", Msg.Required, ("Field", "Reason"));   // BR-VH-023: each with date and reason
        else if (reason.Length > 500) Add("reason", Msg.MaxLength, ("Field", "Reason"), ("Max", 500));
        if (request.Reference is { Length: > 60 }) Add("reference", Msg.MaxLength, ("Field", "Reference"), ("Max", 60));
        if (vehicle.Status == VehicleStatuses.Retired && kind == DisposalKinds.Retire) Add("kind", Msg.VhWrongStatus, ("Status", vehicle.Status), ("Action", "retired again"));

        if (kind is DisposalKinds.Sell or DisposalKinds.Transfer)
        {
            if (request.CounterpartyId is not > 0) Add("counterpartyId", Msg.Required, ("Field", kind == DisposalKinds.Sell ? "Buyer" : "New party"));
            else if (await ctx.Partners.FindAsync(request.CounterpartyId.Value) is not { IsAvailable: true }) Add("counterpartyId", Msg.Invalid, ("Field", kind == DisposalKinds.Sell ? "Buyer" : "New party"));
        }
        if (kind == DisposalKinds.Sell)
        {
            if (request.Amount is null) Add("amount", Msg.Required, ("Field", "Sale amount"));
            else if (request.Amount < 0 || request.Amount >= 10_000_000_000_000m) Add("amount", Msg.Invalid, ("Field", "Sale amount"));
        }
        if (errors.Count > 0) throw new ValidationException(errors);

        var to = DisposalKinds.StatusOf(kind);
        var from = vehicle.Status;
        await ctx.Db.InTransactionAsync(async ct =>
        {
            // The arrangement and the default driver end with the vehicle's time in the fleet.
            var open = await ctx.Db.Relations.FirstOrDefaultAsync(r => r.TenantId == ctx.Tenant && r.VehicleId == id && r.EffectiveTo == null, ct);
            if (open is not null) open.EffectiveTo = date < open.EffectiveFrom ? open.EffectiveFrom : date;
            var driver = await ctx.Db.Assignments.FirstOrDefaultAsync(a => a.TenantId == ctx.Tenant && a.VehicleId == id && a.EffectiveTo == null, ct);
            if (driver is not null) { driver.EffectiveTo = date < driver.EffectiveFrom ? driver.EffectiveFrom : date; driver.EndReason = $"Vehicle {to.ToLowerInvariant()}"; }

            await recurringCharges.EndDateForDisposalAsync(id, date, ct);   // BR-VH-034

            vehicle.Status = to;
            vehicle.DefaultDriverId = null;
            vehicle.ModifiedBy = caller.UserId;
            vehicle.ModifiedOn = DateTime.UtcNow;
            ctx.LogEvent(vehicle, LifecycleEvents.Disposal, date, caller, fromStatus: from, toStatus: to, reason: reason, reference: request.Reference,
                counterpartyId: request.CounterpartyId, amount: kind == DisposalKinds.Sell ? request.Amount : null);
            await ctx.Db.SaveChangesAsync(ct);
        });
        return await vehicles.GetAsync(id);
    }

    // ── History ─────────────────────────────────────────────────────────────────────

    /// <summary>Everything that happened to the vehicle: field changes (from the audit trail, newest first) and its lifecycle. A value the caller may not see comes back marked, without the value (BR-SEC-003).</summary>
    public async Task<VehicleHistory> HistoryAsync(int id, int page, int pageSize, ClaimsPrincipal user)
    {
        await ctx.LoadReadOnlyAsync(id);
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize <= 0 ? 50 : pageSize, 1, MaxPageSize);
        var root = id.ToString();

        var changes = ctx.Db.Set<AuditEntry>().AsNoTracking().Where(a => a.TenantId == ctx.Tenant && a.RootEntity == "Vehicle" && a.RootRecordId == root);
        var total = await changes.CountAsync();
        var rows = await changes.OrderByDescending(a => a.OccurredAt).ThenByDescending(a => a.AuditEntryID).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        var items = rows.Select(a =>
        {
            var hidden = a.RequiredPermission is { } permission && !user.HasPermission(permission);
            return new VehicleHistoryChange
            {
                Id = a.AuditEntryID, OccurredAt = a.OccurredAt, GroupId = a.GroupId, UserName = a.UserName, Entity = a.Entity, RecordId = a.RecordId, Action = a.Action,
                Field = a.Field, Reason = a.Reason, Restricted = hidden, OldValue = hidden ? null : a.OldValue, NewValue = hidden ? null : a.NewValue
            };
        }).ToList();

        var lifecycle = await ctx.Db.Lifecycle.AsNoTracking().Where(l => l.TenantId == ctx.Tenant && l.VehicleId == id)
            .OrderByDescending(l => l.OccurredOn).ThenByDescending(l => l.VehicleLifecycleEntryId).ToListAsync();
        var refs = await ctx.RefsAsync(lifecycle.Select(l => l.CounterpartyId));
        var canSeeAmount = user.HasPermission(PermissionCodes.VEH_FIELD_COST_VIEW);

        return new VehicleHistory
        {
            Changes = new PaginatedResponse<VehicleHistoryChange> { Items = items, TotalCount = total, Page = page, PageSize = pageSize },
            Lifecycle = lifecycle.Select(l => new LifecycleItem
            {
                EventType = l.EventType, FromStatus = l.FromStatus, ToStatus = l.ToStatus, FromCategory = l.FromCategory, ToCategory = l.ToCategory, EffectiveDate = l.EffectiveDate,
                Reason = l.Reason, Reference = l.Reference, Counterparty = VehicleContext.Ref(refs, l.CounterpartyId), Amount = canSeeAmount ? l.Amount : null, UserName = l.UserName, OccurredOn = l.OccurredOn
            }).ToList()
        };
    }
}
