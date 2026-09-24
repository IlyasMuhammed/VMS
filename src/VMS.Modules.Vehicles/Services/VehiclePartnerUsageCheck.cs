using Microsoft.EntityFrameworkCore;
using VMS.Modules.Vehicles.Data;
using VMS.Modules.Vehicles.Domain;
using VMS.Shared.Common;
using VMS.Shared.Partners;

namespace VMS.Modules.Vehicles.Services;

/// <summary>
/// Tells the partner module what vehicles still depend on a partner, so a partner cannot be set Inactive, lose a role or change
/// its party type while a vehicle points at it (BR-BP-011, BR-BP-012, BR-BP-014). Draft and disposed vehicles do not count:
/// nothing operational rests on them.
/// </summary>
internal sealed class VehiclePartnerUsageCheck(VehicleDbContext db, ITenantContext tenantContext) : IPartnerUsageCheck
{
    public async Task<IReadOnlyList<PartnerUsage>> FindBlockingAsync(int partnerId, PartnerIntent intent, string? roleCode, CancellationToken cancellationToken = default)
    {
        var tenant = tenantContext.TenantId;
        var found = new List<PartnerUsage>();
        bool Blocks(params string[] roles) => intent != PartnerIntent.RemoveRole || (roleCode is not null && roles.Contains(roleCode));

        var fleet = db.Vehicles.AsNoTracking().Where(v => v.TenantId == tenant && v.IsInFleet);

        // The default driver.
        if (Blocks(PartnerRoleCodes.Driver))
            foreach (var v in await fleet.Where(v => v.DefaultDriverId == partnerId).Select(v => v.RegistrationNo).ToListAsync(cancellationToken))
                found.Add(new PartnerUsage("Vehicle", v, $"Default driver of {v}"));

        // The other side of the vehicle's ownership category.
        var relations = await (from r in db.Relations.AsNoTracking()
                               join v in fleet on r.VehicleId equals v.VehicleId
                               where r.TenantId == tenant && r.EffectiveTo == null && r.CounterpartyId == partnerId
                               select new { v.RegistrationNo, r.Category }).ToListAsync(cancellationToken);
        foreach (var r in relations)
        {
            var (roles, label) = r.Category switch
            {
                OwnershipCategories.BankLeased => (new[] { PartnerRoleCodes.Bank }, "Bank"),
                OwnershipCategories.CustomerArrangement => (new[] { PartnerRoleCodes.Customer, PartnerRoleCodes.RunningCustomer }, "Customer"),
                OwnershipCategories.Rented => (Array.Empty<string>(), "Lessor"),
                _ => (Array.Empty<string>(), "Sharing partner")
            };
            // Any role suits a lessor or a sharing partner, so only deactivating (or changing the party type) is stopped for those.
            if (roles.Length == 0 ? intent != PartnerIntent.RemoveRole : Blocks(roles))
                found.Add(new PartnerUsage("Vehicle", r.RegistrationNo, $"{label} of {r.RegistrationNo}"));
        }
        if (Blocks(PartnerRoleCodes.FuelCardCompany))
            foreach (var v in await fleet.Where(v => v.FuelCardCompanyId == partnerId).Select(v => v.RegistrationNo).ToListAsync(cancellationToken))
                found.Add(new PartnerUsage("Vehicle", v, $"Fuel card company of {v}"));
        if (Blocks(PartnerRoleCodes.TrackerCompany))
            foreach (var v in await fleet.Where(v => v.TrackerCompanyId == partnerId).Select(v => v.RegistrationNo).ToListAsync(cancellationToken))
                found.Add(new PartnerUsage("Vehicle", v, $"Tracker company of {v}"));

        if (Blocks(PartnerRoleCodes.Vendor, PartnerRoleCodes.BodyMaker))
        {
            var items = await (from i in db.Items.AsNoTracking()
                               join v in fleet on i.VehicleId equals v.VehicleId
                               where i.TenantId == tenant && i.Status == ItemStatuses.Attached && i.SupplierId == partnerId
                               select new { v.RegistrationNo, i.Description }).ToListAsync(cancellationToken);
            found.AddRange(items.Select(i => new PartnerUsage("Vehicle", i.RegistrationNo, $"Supplier of {i.Description} on {i.RegistrationNo}")));
        }

        return found;
    }
}
