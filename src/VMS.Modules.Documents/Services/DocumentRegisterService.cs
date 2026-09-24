using Microsoft.EntityFrameworkCore;
using VMS.Modules.Documents.Data;
using VMS.Modules.Documents.Domain;
using VMS.Modules.Documents.Models;
using VMS.Shared.Common;
using VMS.Shared.Partners;
using VMS.Shared.Time;
using VMS.Shared.Vehicles;

namespace VMS.Modules.Documents.Services;

public interface IDocumentRegisterService
{
    /// <summary>Every current document across both owners (§23A.4's Document Register). This is the compliance officer's screen.</summary>
    Task<List<RegisterRow>> RegisterAsync(RegisterQuery query);

    /// <summary>Every owner lacking a document its type marks mandatory or periodic — the gap an expiry report alone cannot show.</summary>
    Task<List<MissingDocumentRow>> MissingAsync(string? ownerType = null);

    /// <summary>Documents due to expire in the given month, for the Expiry Calendar's month view.</summary>
    Task<List<CalendarEntry>> CalendarAsync(int year, int month);
}

internal sealed class DocumentRegisterService(DocumentDbContext db, ITenantContext tenantContext, IOperatingClock clock, IPartnerDirectory partners, IVehicleDirectory vehicles, IDocumentTypeService typeService) : IDocumentRegisterService
{
    private Guid Tenant => tenantContext.TenantId;

    public async Task<List<RegisterRow>> RegisterAsync(RegisterQuery query)
    {
        var today = await clock.TodayAsync(Tenant);
        var rows = db.Documents.AsNoTracking().Where(d => d.TenantId == Tenant && d.IsCurrent);
        if (query.OwnerType is { } ownerType) rows = rows.Where(d => d.OwnerType == ownerType);
        if (query.DocumentTypeId is { } typeId) rows = rows.Where(d => d.DocumentTypeId == typeId);
        if (query.Status is { } status) rows = rows.Where(d => d.Status == status);
        if (query.ExpiringWithinDays is { } days) { var cutoff = today.AddDays(days); rows = rows.Where(d => d.ExpiryDate != null && d.ExpiryDate <= cutoff && d.ExpiryDate >= today); }

        var list = await rows.OrderBy(d => d.ExpiryDate ?? DateOnly.MaxValue).ToListAsync();
        var typeNames = await db.Types.AsNoTracking().Where(t => t.TenantId == Tenant).ToDictionaryAsync(t => t.DocumentTypeId, t => t.Name);
        var names = await OwnerNamesAsync(list.Select(d => (d.OwnerType, d.OwnerId)));

        return list.Select(d => new RegisterRow
        {
            DocumentId = d.DocumentId, DocumentCode = d.DocumentCode, OwnerType = d.OwnerType, OwnerId = d.OwnerId, OwnerName = names.GetValueOrDefault((d.OwnerType, d.OwnerId), $"#{d.OwnerId}"),
            DocumentTypeId = d.DocumentTypeId, DocumentTypeName = typeNames.GetValueOrDefault(d.DocumentTypeId, "Unknown type"), Status = d.Status, ExpiryDate = d.ExpiryDate,
            DaysRemaining = d.ExpiryDate is { } exp ? exp.DayNumber - today.DayNumber : null,
        }).ToList();
    }

    public async Task<List<MissingDocumentRow>> MissingAsync(string? ownerType = null)
    {
        await typeService.ListAsync();
        var types = await db.Types.AsNoTracking().Where(t => t.TenantId == Tenant && t.IsActive && t.MandatoryLevel != MandatoryLevels.None).ToListAsync();
        if (ownerType is not null) types = types.Where(t => (t.AppliesTo & (ownerType == DocumentOwnerTypes.Vehicle ? AppliesTo.Vehicle : AppliesTo.BusinessPartner)) != 0).ToList();

        var result = new List<MissingDocumentRow>();

        if (types.Any(t => t.AppliesTo.HasFlag(AppliesTo.Vehicle)) && ownerType != DocumentOwnerTypes.BusinessPartner)
        {
            var vehicleTypes = types.Where(t => t.AppliesTo.HasFlag(AppliesTo.Vehicle)).ToList();
            var allVehicles = await AllVehiclesAsync();
            var current = await db.Documents.AsNoTracking().Where(d => d.TenantId == Tenant && d.OwnerType == DocumentOwnerTypes.Vehicle && d.IsCurrent)
                .Select(d => new { d.OwnerId, d.DocumentTypeId }).ToListAsync();
            var have = current.Select(c => (c.OwnerId, c.DocumentTypeId)).ToHashSet();
            foreach (var v in allVehicles)
                foreach (var t in vehicleTypes.Where(t => !have.Contains((v.Id, t.DocumentTypeId))))
                    result.Add(new MissingDocumentRow { OwnerType = DocumentOwnerTypes.Vehicle, OwnerId = v.Id, OwnerName = v.RegistrationNo, DocumentTypeCode = t.Code, DocumentTypeName = t.Name, Required = t.MandatoryLevel == MandatoryLevels.Required });
        }

        if (types.Any(t => t.AppliesTo.HasFlag(AppliesTo.BusinessPartner)) && ownerType != DocumentOwnerTypes.Vehicle)
        {
            var partnerTypes = types.Where(t => t.AppliesTo.HasFlag(AppliesTo.BusinessPartner)).ToList();
            var allPartners = await AllPartnersAsync();
            var current = await db.Documents.AsNoTracking().Where(d => d.TenantId == Tenant && d.OwnerType == DocumentOwnerTypes.BusinessPartner && d.IsCurrent)
                .Select(d => new { d.OwnerId, d.DocumentTypeId }).ToListAsync();
            var have = current.Select(c => (c.OwnerId, c.DocumentTypeId)).ToHashSet();
            foreach (var p in allPartners)
                foreach (var t in partnerTypes.Where(t => (t.PartnerRole is null || p.Roles.Contains(t.PartnerRole)) && !have.Contains((p.Id, t.DocumentTypeId))))
                    result.Add(new MissingDocumentRow { OwnerType = DocumentOwnerTypes.BusinessPartner, OwnerId = p.Id, OwnerName = p.DisplayName, DocumentTypeCode = t.Code, DocumentTypeName = t.Name, Required = t.MandatoryLevel == MandatoryLevels.Required });
        }

        return result.OrderByDescending(r => r.Required).ThenBy(r => r.OwnerName).ToList();
    }

    public async Task<List<CalendarEntry>> CalendarAsync(int year, int month)
    {
        var from = new DateOnly(year, month, 1);
        var to = from.AddMonths(1).AddDays(-1);
        var rows = await db.Documents.AsNoTracking()
            .Where(d => d.TenantId == Tenant && d.IsCurrent && d.ExpiryDate != null && d.ExpiryDate >= from && d.ExpiryDate <= to)
            .OrderBy(d => d.ExpiryDate).ToListAsync();
        var typeNames = await db.Types.AsNoTracking().Where(t => t.TenantId == Tenant).ToDictionaryAsync(t => t.DocumentTypeId, t => t.Name);
        var names = await OwnerNamesAsync(rows.Select(d => (d.OwnerType, d.OwnerId)));

        return rows.Select(d => new CalendarEntry
        {
            Date = d.ExpiryDate!.Value, OwnerType = d.OwnerType, OwnerId = d.OwnerId, OwnerName = names.GetValueOrDefault((d.OwnerType, d.OwnerId), $"#{d.OwnerId}"),
            DocumentTypeName = typeNames.GetValueOrDefault(d.DocumentTypeId, "Unknown type"), DocumentId = d.DocumentId,
        }).ToList();
    }

    // ── Owner lookups ────────────────────────────────────────────────────────────

    private async Task<Dictionary<(string OwnerType, int OwnerId), string>> OwnerNamesAsync(IEnumerable<(string OwnerType, int OwnerId)> owners)
    {
        var list = owners.Distinct().ToList();
        var vehicleIds = list.Where(o => o.OwnerType == DocumentOwnerTypes.Vehicle).Select(o => o.OwnerId).ToList();
        var partnerIds = list.Where(o => o.OwnerType == DocumentOwnerTypes.BusinessPartner).Select(o => o.OwnerId).ToList();
        var v = vehicleIds.Count > 0 ? await vehicles.FindManyAsync(vehicleIds) : new Dictionary<int, VehicleInfo>();
        var p = partnerIds.Count > 0 ? await partners.FindManyAsync(partnerIds) : new Dictionary<int, PartnerInfo>();

        var map = new Dictionary<(string, int), string>();
        foreach (var (id, info) in v) map[(DocumentOwnerTypes.Vehicle, id)] = info.RegistrationNo;
        foreach (var (id, info) in p) map[(DocumentOwnerTypes.BusinessPartner, id)] = info.DisplayName;
        return map;
    }

    private async Task<List<VehicleInfo>> AllVehiclesAsync() => (await vehicles.AllAsync()).ToList();

    private async Task<List<PartnerInfo>> AllPartnersAsync() => (await partners.AllAsync()).ToList();
}
