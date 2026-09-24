using Microsoft.EntityFrameworkCore;
using VMS.Modules.Vehicles.Domain;
using VMS.Shared.Exceptions;

namespace VMS.Modules.Vehicles.Services;

/// <summary>One way a vehicle is linked to a partner: the vehicle, what the partner is to it, and for how long.</summary>
public class LinkedVehicle
{
    public int VehicleId { get; set; }
    public string VehicleCode { get; set; } = string.Empty;
    public string RegistrationNo { get; set; } = string.Empty;
    public string VehicleStatus { get; set; } = string.Empty;
    /// <summary>Owner, Driver, FuelCardCompany, TrackerCompany, Supplier.</summary>
    public string Link { get; set; } = string.Empty;
    /// <summary>For an owner link the category (Rented, BankLeased, …); for a supplier the item.</summary>
    public string? Detail { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    /// <summary>True while the link holds now.</summary>
    public bool IsCurrent { get; set; }
}

public interface ILinkedVehiclesService
{
    Task<List<LinkedVehicle>> ForPartnerAsync(int partnerId);
}

/// <summary>The vehicles a partner has to do with, past and present (FSD §9.2 Linked Vehicles tab): as the other side of an ownership category, as a driver, as fuel card or tracker company, as the supplier of an attached item.</summary>
internal sealed class LinkedVehiclesService(VehicleContext ctx) : ILinkedVehiclesService
{
    public async Task<List<LinkedVehicle>> ForPartnerAsync(int partnerId)
    {
        if (await ctx.Partners.FindAsync(partnerId) is null) throw new NotFoundException("Partner not found.");
        var db = ctx.Db;
        var tenant = ctx.Tenant;
        var vehicles = db.Vehicles.AsNoTracking().Where(v => v.TenantId == tenant && !v.IsDeleted);
        var found = new List<LinkedVehicle>();

        found.AddRange((await (from r in db.Relations.AsNoTracking() join v in vehicles on r.VehicleId equals v.VehicleId
                               where r.TenantId == tenant && r.CounterpartyId == partnerId
                               select new { v.VehicleId, v.VehicleCode, v.RegistrationNo, v.Status, r.Category, r.EffectiveFrom, r.EffectiveTo }).ToListAsync())
            .Select(x => new LinkedVehicle
            {
                VehicleId = x.VehicleId, VehicleCode = x.VehicleCode, RegistrationNo = x.RegistrationNo, VehicleStatus = x.Status, Link = "Owner", Detail = x.Category,
                From = x.EffectiveFrom, To = x.EffectiveTo, IsCurrent = x.EffectiveTo is null
            }));

        found.AddRange((await (from a in db.Assignments.AsNoTracking() join v in vehicles on a.VehicleId equals v.VehicleId
                               where a.TenantId == tenant && a.DriverId == partnerId
                               select new { v.VehicleId, v.VehicleCode, v.RegistrationNo, v.Status, a.EffectiveFrom, a.EffectiveTo }).ToListAsync())
            .Select(x => new LinkedVehicle
            {
                VehicleId = x.VehicleId, VehicleCode = x.VehicleCode, RegistrationNo = x.RegistrationNo, VehicleStatus = x.Status, Link = "Driver",
                From = x.EffectiveFrom, To = x.EffectiveTo, IsCurrent = x.EffectiveTo is null
            }));

        // A fuel card or tracker company is a plain field of the vehicle: it holds while the vehicle is in the fleet.
        foreach (var (link, query) in new (string, IQueryable<Vehicle>)[]
        {
            ("FuelCardCompany", vehicles.Where(v => v.FuelCardCompanyId == partnerId)),
            ("TrackerCompany", vehicles.Where(v => v.TrackerCompanyId == partnerId))
        })
            found.AddRange((await query.ToListAsync()).Select(v => new LinkedVehicle
            {
                VehicleId = v.VehicleId, VehicleCode = v.VehicleCode, RegistrationNo = v.RegistrationNo, VehicleStatus = v.Status, Link = link, IsCurrent = v.IsInFleet
            }));

        found.AddRange((await (from i in db.Items.AsNoTracking() join v in vehicles on i.VehicleId equals v.VehicleId
                               where i.TenantId == tenant && i.SupplierId == partnerId
                               select new { v.VehicleId, v.VehicleCode, v.RegistrationNo, v.Status, i.Description, i.InstallationDate, i.DetachedOn, ItemStatus = i.Status }).ToListAsync())
            .Select(x => new LinkedVehicle
            {
                VehicleId = x.VehicleId, VehicleCode = x.VehicleCode, RegistrationNo = x.RegistrationNo, VehicleStatus = x.Status, Link = "Supplier", Detail = x.Description,
                From = x.InstallationDate, To = x.DetachedOn, IsCurrent = x.ItemStatus == ItemStatuses.Attached
            }));

        return found.OrderByDescending(l => l.IsCurrent).ThenBy(l => l.RegistrationNo).ThenBy(l => l.Link).ThenByDescending(l => l.From).ToList();
    }
}
