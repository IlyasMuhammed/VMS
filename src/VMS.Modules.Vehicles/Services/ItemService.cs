using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using VMS.Modules.Vehicles.Domain;
using VMS.Modules.Vehicles.Models;
using VMS.Shared.Authorization;
using VMS.Shared.Exceptions;
using VMS.Shared.Lookups;
using VMS.Shared.Messages;
using VMS.Shared.Numbering;
using VMS.Shared.Partners;

namespace VMS.Modules.Vehicles.Services;

public interface IItemService
{
    Task<List<ItemModel>> ListAsync(int vehicleId, bool includeGone);
    Task<ItemModel> AttachAsync(int vehicleId, AttachItemRequest request, VehicleCaller caller);
    Task<ItemModel> DetachAsync(int vehicleId, int itemId, DetachItemRequest request, VehicleCaller caller);
    Task<ItemModel> TransferAsync(int vehicleId, int itemId, TransferItemRequest request, VehicleCaller caller);
}

/// <summary>Attached items (FSD §20.1): things fitted to a vehicle that have a life of their own, and can be detached or moved to another vehicle (BR-VH-013, BR-VH-014).</summary>
internal sealed class ItemService(VehicleContext ctx, ILookupReader lookups) : IItemService
{
    private static string? Blank(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    // ── Reading ─────────────────────────────────────────────────────────────────────

    public async Task<List<ItemModel>> ListAsync(int vehicleId, bool includeGone)
    {
        await ctx.LoadReadOnlyAsync(vehicleId);
        var query = ctx.Db.Items.AsNoTracking().Where(i => i.TenantId == ctx.Tenant && i.VehicleId == vehicleId);
        if (!includeGone) query = query.Where(i => i.Status == ItemStatuses.Attached);
        var items = await query.OrderBy(i => i.Status == ItemStatuses.Attached ? 0 : 1).ThenByDescending(i => i.InstallationDate).ThenBy(i => i.VehicleAttachedItemId).ToListAsync();
        return await ToModelsAsync(items);
    }

    private async Task<List<ItemModel>> ToModelsAsync(IReadOnlyCollection<VehicleAttachedItem> items)
    {
        var types = await lookups.FindManyAsync(PlatformLookups.AttachedItemType, items.Select(i => i.ItemTypeId));
        var refs = await ctx.RefsAsync(items.Select(i => i.SupplierId));
        return items.Select(i => new ItemModel
        {
            Id = i.VehicleAttachedItemId, VehicleId = i.VehicleId, ItemTypeId = i.ItemTypeId, ItemType = types.GetValueOrDefault(i.ItemTypeId)?.Description,
            Description = i.Description, SerialNo = i.SerialNo, Supplier = VehicleContext.Ref(refs, i.SupplierId), InstallationDate = i.InstallationDate, Cost = i.Cost,
            WarrantyUntil = i.WarrantyUntil, Condition = i.Condition, Status = i.Status, DetachedOn = i.DetachedOn, DetachReason = i.DetachReason,
            TransferredFromItemId = i.TransferredFromItemId, TransferredToVehicleId = i.TransferredToVehicleId
        }).ToList();
    }

    // ── Attaching ───────────────────────────────────────────────────────────────────

    /// <summary>An item checked and ready to write, with the name of its type (for the ledger) and whatever was wrong with it.</summary>
    internal sealed record PreparedItem(VehicleAttachedItem Item, string TypeName, List<ValidationError> Errors);

    /// <summary>
    /// Checks one item against the rules of FSD §20.1 and builds it. Used to attach an item to a vehicle in the fleet and to write the items of
    /// a Draft when it is activated, so the two can never disagree about what an item is. <paramref name="prefix"/> puts an item's field errors
    /// under its place in a list (<c>items[0].</c>). A serial number is judged against the fleet; a repeat inside one list is the caller's to catch.
    /// </summary>
    internal async Task<PreparedItem> PrepareAsync(int vehicleId, AttachItemRequest request, DateOnly? acquired, DateOnly today, DateOnly defaultDate, VehicleCaller caller, string prefix = "")
    {
        var installed = request.InstallationDate ?? defaultDate;
        var description = request.Description?.Trim() ?? string.Empty;
        var serial = Blank(request.SerialNo)?.ToUpperInvariant();
        var errors = new List<ValidationError>();
        void Add(string field, string code, params (string Name, object? Value)[] values) => errors.Add(ctx.Messages.Error(prefix + field, code, values));

        var itemType = request.ItemTypeId > 0 ? await lookups.FindAsync(PlatformLookups.AttachedItemType, request.ItemTypeId) : null;
        if (itemType is not { IsActive: true }) Add("itemTypeId", Msg.Invalid, ("Field", "Item type"));
        if (description.Length == 0) Add("description", Msg.Required, ("Field", "Description"));
        else if (description.Length > 150) Add("description", Msg.MaxLength, ("Field", "Description"), ("Max", 150));
        if (serial is { Length: > 60 }) Add("serialNo", Msg.MaxLength, ("Field", "Serial number"), ("Max", 60));
        if (installed > today) Add("installationDate", Msg.NotFuture, ("Field", "Installation date"));
        else if (acquired is { } day && installed < day) Add("installationDate", Msg.VhItemBeforeAcquisition);   // BR-VH-018
        if (request.Cost < 0 || request.Cost >= 10_000_000_000_000m) Add("cost", Msg.Invalid, ("Field", "Cost"));
        if (request.WarrantyUntil is { } warranty && warranty <= installed) Add("warrantyUntil", Msg.VhEndBeforeStart);
        if (request.Condition is not null && !ItemConditions.All.Contains(request.Condition)) Add("condition", Msg.OneOf, ("Field", "Condition"), ("Allowed", string.Join(", ", ItemConditions.All)));

        if (request.SupplierId is > 0)
        {
            var partner = await ctx.Partners.FindAsync(request.SupplierId.Value);
            if (partner is null || !partner.IsAvailable) Add("supplierId", Msg.Invalid, ("Field", "Supplier"));
            else if (!partner.HasRole(PartnerRoleCodes.Vendor) && !partner.HasRole(PartnerRoleCodes.BodyMaker))
                Add("supplierId", Msg.VhCounterpartyLacksRole, ("PartnerName", partner.DisplayName), ("Role", "Vendor or Body Maker"));
        }
        if (serial is not null && await SerialHolderAsync(serial, excludeItemId: 0) is { } holder)
            Add("serialNo", Msg.VhChassisOrEngineDuplicate, ("field", "serial number"), ("VehicleCode", holder));   // BR-VH-014

        var item = new VehicleAttachedItem
        {
            VehicleId = vehicleId, ItemTypeId = request.ItemTypeId, Description = description, SerialNo = serial, SupplierId = request.SupplierId is > 0 ? request.SupplierId : null,
            InstallationDate = installed, WarrantyUntil = request.WarrantyUntil, Condition = request.Condition, Status = ItemStatuses.Attached,
            Cost = caller.Has(PermissionCodes.VEH_FIELD_COST_VIEW) ? request.Cost : null,   // a cost the caller may not see is not theirs to set
            CreatedBy = caller.UserId, CreatedOn = DateTime.UtcNow
        };
        return new PreparedItem(item, itemType?.Description ?? string.Empty, errors);
    }

    public async Task<ItemModel> AttachAsync(int vehicleId, AttachItemRequest request, VehicleCaller caller)
    {
        var vehicle = await ctx.LoadAsync(vehicleId);
        if (ctx.NotInFleet(vehicle, "given attached items") is { } wrong) throw wrong;

        var today = await ctx.TodayAsync();
        var prepared = await PrepareAsync(vehicleId, request, vehicle.AcquisitionDate, today, today, caller);
        if (prepared.Errors.Count > 0) throw new ValidationException(prepared.Errors);
        var item = prepared.Item;

        try
        {
            ctx.Db.Items.Add(item);
            // A cost is a major expense of the vehicle (§19), posted with the item in the same save, so the ledger and the item can never disagree.
            if (OpeningPostings.ForItem(prepared.TypeName, item.Description, item.Cost, item.InstallationDate, item.SupplierId) is { } posting)
                ctx.Db.Transactions.Add(new VehicleTransaction
                {
                    VehicleId = vehicleId, Type = posting.Type, SubType = posting.SubType, Amount = posting.Amount, TransactionDate = posting.Date, PartnerId = posting.PartnerId,
                    Reference = posting.Reference.Length <= 120 ? posting.Reference : posting.Reference[..120], Source = TransactionSources.Manual, IsSystemGenerated = true,
                    CreatedBy = caller.UserId, CreatedOn = DateTime.UtcNow
                });
            await ctx.Db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw new ValidationException(ctx.Messages.Error("serialNo", Msg.VhChassisOrEngineDuplicate, ("field", "serial number"), ("VehicleCode", await SerialHolderAsync(item.SerialNo ?? "", 0) ?? "another vehicle")));
        }
        return (await ToModelsAsync([item]))[0];
    }
    /// <summary>The code of the vehicle that has an attached item with this serial number now, if any.</summary>
    private async Task<string?> SerialHolderAsync(string serial, int excludeItemId) =>
        await (from i in ctx.Db.Items.AsNoTracking()
               join v in ctx.Db.Vehicles.AsNoTracking() on i.VehicleId equals v.VehicleId
               where i.TenantId == ctx.Tenant && i.Status == ItemStatuses.Attached && i.SerialNo == serial && i.VehicleAttachedItemId != excludeItemId
               select v.VehicleCode).FirstOrDefaultAsync();

    // ── Detaching ───────────────────────────────────────────────────────────────────

    private async Task<VehicleAttachedItem> LoadItemAsync(int vehicleId, int itemId) =>
        await ctx.Db.Items.FirstOrDefaultAsync(i => i.TenantId == ctx.Tenant && i.VehicleId == vehicleId && i.VehicleAttachedItemId == itemId)
        ?? throw new NotFoundException("Attached item not found.");

    public async Task<ItemModel> DetachAsync(int vehicleId, int itemId, DetachItemRequest request, VehicleCaller caller)
    {
        var vehicle = await ctx.LoadAsync(vehicleId);
        var item = await LoadItemAsync(vehicleId, itemId);
        if (item.Status != ItemStatuses.Attached) throw new ConflictException($"This item is already {item.Status.ToLowerInvariant()}.");

        var today = await ctx.TodayAsync();
        var date = request.Date ?? today;
        var reason = Blank(request.Reason);
        var errors = new List<ValidationError>();
        if (date > today) errors.Add(ctx.Messages.Error("date", Msg.NotFuture, ("Field", "Date")));
        else if (date < item.InstallationDate) errors.Add(ctx.Messages.Error("date", Msg.Min, ("Field", "Date"), ("Min", item.InstallationDate.ToString("yyyy-MM-dd"))));
        if (reason is null) errors.Add(ctx.Messages.Error("reason", Msg.Required, ("Field", "Reason")));   // BR-VH-013: with a date and reason
        else if (reason.Length > 500) errors.Add(ctx.Messages.Error("reason", Msg.MaxLength, ("Field", "Reason"), ("Max", 500)));
        if (errors.Count > 0) throw new ValidationException(errors);

        item.Status = ItemStatuses.Detached;
        item.DetachedOn = date;
        item.DetachReason = reason;
        await ctx.Db.SaveChangesAsync();
        _ = vehicle;
        return (await ToModelsAsync([item]))[0];
    }

    // ── Transferring ────────────────────────────────────────────────────────────────

    /// <summary>Moves the item to another vehicle: closed on this one, opened on the target with the same serial number, each pointing at the other so the chain can be followed (BR-VH-013).</summary>
    public async Task<ItemModel> TransferAsync(int vehicleId, int itemId, TransferItemRequest request, VehicleCaller caller)
    {
        await ctx.LoadAsync(vehicleId);
        var item = await LoadItemAsync(vehicleId, itemId);
        if (item.Status != ItemStatuses.Attached) throw new ConflictException($"This item is already {item.Status.ToLowerInvariant()}.");

        var today = await ctx.TodayAsync();
        var date = request.Date ?? today;
        var reason = Blank(request.Reason);
        var errors = new List<ValidationError>();

        var target = request.TargetVehicleId == vehicleId ? null : await ctx.Own().FirstOrDefaultAsync(v => v.VehicleId == request.TargetVehicleId);
        if (target is null) errors.Add(ctx.Messages.Error("targetVehicleId", Msg.Invalid, ("Field", "Target vehicle")));
        else if (ctx.NotInFleet(target, "given attached items") is { } wrongTarget) throw wrongTarget;

        if (date > today) errors.Add(ctx.Messages.Error("date", Msg.NotFuture, ("Field", "Date")));
        else if (date < item.InstallationDate) errors.Add(ctx.Messages.Error("date", Msg.Min, ("Field", "Date"), ("Min", item.InstallationDate.ToString("yyyy-MM-dd"))));
        else if (target?.AcquisitionDate is { } acquired && date < acquired) errors.Add(ctx.Messages.Error("date", Msg.VhItemBeforeAcquisition));
        if (reason is { Length: > 500 }) errors.Add(ctx.Messages.Error("reason", Msg.MaxLength, ("Field", "Reason"), ("Max", 500)));
        if (errors.Count > 0) throw new ValidationException(errors);

        var moved = new VehicleAttachedItem
        {
            VehicleId = target!.VehicleId, ItemTypeId = item.ItemTypeId, Description = item.Description, SerialNo = item.SerialNo, SupplierId = item.SupplierId,
            InstallationDate = date, Cost = null, WarrantyUntil = item.WarrantyUntil, Condition = item.Condition, Status = ItemStatuses.Attached,
            TransferredFromItemId = item.VehicleAttachedItemId, CreatedBy = caller.UserId, CreatedOn = DateTime.UtcNow
        };
        await ctx.Db.InTransactionAsync(async ct =>
        {
            item.Status = ItemStatuses.Transferred;   // first: the serial number may be on one attached item at a time
            item.DetachedOn = date;
            item.DetachReason = reason ?? $"Transferred to {target.RegistrationNo}";
            item.TransferredToVehicleId = target.VehicleId;
            await ctx.Db.SaveChangesAsync(ct);
            ctx.Db.Items.Add(moved);
            await ctx.Db.SaveChangesAsync(ct);
        });
        return (await ToModelsAsync([moved]))[0];
    }
}
