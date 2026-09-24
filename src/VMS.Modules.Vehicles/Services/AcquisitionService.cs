using Microsoft.EntityFrameworkCore;
using VMS.Modules.Vehicles.Domain;
using VMS.Modules.Vehicles.Models;
using VMS.Shared.Authorization;
using VMS.Shared.Exceptions;
using VMS.Shared.Messages;
using VMS.Shared.Numbering;

namespace VMS.Modules.Vehicles.Services;

public interface IAcquisitionService
{
    Task<AcquisitionModel> GetAsync(int vehicleId);
    Task<AcquisitionModel> SaveAsync(int vehicleId, SaveAcquisitionRequest request, VehicleCaller caller);
}

/// <summary>
/// The acquisition block of a Draft vehicle (FSD §18.1): when and how it was acquired, what was paid, the registration cost.
/// It is only held here. The opening postings are written when the vehicle is activated (§19), and after that a mistake is
/// corrected by a reversing entry, never by editing the block (BR-VH-011).
/// </summary>
internal sealed class AcquisitionService(VehicleContext ctx) : IAcquisitionService
{
    internal const decimal MaxAmount = 10_000_000_000_000m;

    private static string? Blank(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    public async Task<AcquisitionModel> GetAsync(int vehicleId)
    {
        var vehicle = await ctx.LoadReadOnlyAsync(vehicleId);
        var block = await ctx.Db.Acquisitions.AsNoTracking().FirstOrDefaultAsync(a => a.TenantId == ctx.Tenant && a.VehicleId == vehicleId);
        var refs = await ctx.RefsAsync([block?.SellerId]);
        return ToModel(vehicle, block, refs);
    }

    private static AcquisitionModel ToModel(Vehicle v, VehicleAcquisition? a, IReadOnlyDictionary<int, PartnerRef> refs) => new()
    {
        VehicleId = v.VehicleId, VehicleStatus = v.Status, AcquisitionDate = v.AcquisitionDate, AcquisitionType = v.AcquisitionType,
        SellerId = a?.SellerId, Seller = VehicleContext.Ref(refs, a?.SellerId), PurchasePrice = a?.PurchasePrice, AmountPaid = a?.AmountPaid,
        PaymentMode = a?.PaymentMode, PaymentReference = a?.PaymentReference, RegistrationCost = a?.RegistrationCost,
        RowVersion = Convert.ToBase64String(v.RowVersion)
    };

    public async Task<AcquisitionModel> SaveAsync(int vehicleId, SaveAcquisitionRequest request, VehicleCaller caller)
    {
        // An amount the caller may not see is not theirs to enter.
        if (!caller.Has(PermissionCodes.VEH_FIELD_COST_VIEW)) throw new ForbiddenException("You do not have permission to enter vehicle costs.");

        var vehicle = await ctx.LoadAsync(vehicleId);
        if (vehicle.Status != VehicleStatuses.Draft)
            throw new ValidationException(ctx.Messages.Error("status", Msg.VhWrongStatus, ("Status", vehicle.Status), ("Action", "given a different acquisition")));

        request.AcquisitionType = Blank(request.AcquisitionType);
        request.PaymentMode = Blank(request.PaymentMode);
        request.PaymentReference = Blank(request.PaymentReference);

        var errors = Validate(request, await ctx.TodayAsync());
        if (string.IsNullOrWhiteSpace(request.RowVersion)) errors.Add(ctx.Messages.Error("rowVersion", Msg.Required, ("Field", "Row version")));

        if (request.SellerId is > 0)
        {
            var seller = await ctx.Partners.FindAsync(request.SellerId.Value);
            var held = await ctx.Db.Acquisitions.AsNoTracking().Where(a => a.TenantId == ctx.Tenant && a.VehicleId == vehicleId).Select(a => a.SellerId).FirstOrDefaultAsync();
            if (seller is null || (!seller.IsAvailable && held != request.SellerId)) errors.Add(ctx.Messages.Error("sellerId", Msg.Invalid, ("Field", "Seller")));
        }
        if (errors.Count > 0) throw new ValidationException(errors);

        try { ctx.Db.Entry(vehicle).Property(v => v.RowVersion).OriginalValue = Convert.FromBase64String(request.RowVersion); }
        catch (FormatException) { throw new ValidationException(ctx.Messages.Error("rowVersion", Msg.Invalid, ("Field", "Row version"))); }

        try
        {
            await ctx.Db.InTransactionAsync(async ct =>
            {
                var now = DateTime.UtcNow;
                vehicle.AcquisitionDate = request.AcquisitionDate;
                vehicle.AcquisitionType = request.AcquisitionType;
                vehicle.ModifiedBy = caller.UserId;
                vehicle.ModifiedOn = now;

                var block = await ctx.Db.Acquisitions.FirstOrDefaultAsync(a => a.TenantId == ctx.Tenant && a.VehicleId == vehicleId, ct);
                if (block is null) ctx.Db.Acquisitions.Add(block = new VehicleAcquisition { VehicleId = vehicleId });
                block.SellerId = request.SellerId is > 0 ? request.SellerId : null;
                block.PurchasePrice = request.PurchasePrice;
                block.AmountPaid = request.AmountPaid;
                block.PaymentMode = request.AmountPaid is > 0 ? request.PaymentMode : null;
                block.PaymentReference = request.AmountPaid is > 0 ? request.PaymentReference : null;
                block.RegistrationCost = request.RegistrationCost;
                block.ModifiedBy = caller.UserId;
                block.ModifiedOn = now;
                await ctx.Db.SaveChangesAsync(ct);
            });
        }
        catch (DbUpdateConcurrencyException) { throw await ctx.StaleAsync(vehicleId); }

        return await GetAsync(vehicleId);
    }

    /// <summary>Every check that needs nothing but what was sent (FSD §18.1). Whether a purchase price is required depends on the ownership category, which is checked when the vehicle is activated.</summary>
    internal List<ValidationError> Validate(SaveAcquisitionRequest r, DateOnly today)
    {
        var errors = new List<ValidationError>();
        void Add(string field, string code, params (string Name, object? Value)[] values) => errors.Add(ctx.Messages.Error(field, code, values));

        if (r.AcquisitionDate is null) Add("acquisitionDate", Msg.Required, ("Field", "Acquisition date"));
        else if (r.AcquisitionDate > today) Add("acquisitionDate", Msg.VhAcquisitionDateFuture);

        if (r.AcquisitionType is null) Add("acquisitionType", Msg.Required, ("Field", "Acquisition type"));
        else if (!AcquisitionTypes.All.Contains(r.AcquisitionType))
            Add("acquisitionType", Msg.OneOf, ("Field", "Acquisition type"), ("Allowed", string.Join(", ", AcquisitionTypes.All)));

        if (r.PurchasePrice is { } price && (price <= 0 || price >= MaxAmount)) Add("purchasePrice", Msg.Invalid, ("Field", "Purchase price"));
        if (r.RegistrationCost is { } reg && (reg < 0 || reg >= MaxAmount)) Add("registrationCost", Msg.Invalid, ("Field", "Registration cost"));

        if (r.PurchasePrice is null)
        {
            // Without a price there is nothing to have paid for.
            if (r.AmountPaid is > 0) Add("amountPaid", Msg.Required, ("Field", "Purchase price"));
        }
        else
        {
            if (r.AmountPaid is null) Add("amountPaid", Msg.Required, ("Field", "Amount paid at creation"));
            else if (r.AmountPaid < 0 || r.AmountPaid >= MaxAmount) Add("amountPaid", Msg.Invalid, ("Field", "Amount paid at creation"));
            else if (r.AmountPaid > r.PurchasePrice) Add("amountPaid", Msg.VhPaidExceedsPrice);   // BR-VH-021
        }

        if (r.AmountPaid is > 0)
        {
            if (r.PaymentMode is null) Add("paymentMode", Msg.Required, ("Field", "Payment mode"));
            else if (!PaymentModes.All.Contains(r.PaymentMode)) Add("paymentMode", Msg.OneOf, ("Field", "Payment mode"), ("Allowed", string.Join(", ", PaymentModes.All)));
        }
        if (r.PaymentReference is { Length: > 60 }) Add("paymentReference", Msg.MaxLength, ("Field", "Payment reference"), ("Max", 60));
        return errors;
    }
}
