using Microsoft.EntityFrameworkCore;
using VMS.Modules.BusinessPartners.Data;
using VMS.Shared.Common;
using VMS.Shared.Partners;

namespace VMS.Modules.BusinessPartners.Services;

/// <summary>Answers other modules' questions about partners (a vehicle's driver, a lease's bank) without letting them read the partner tables.</summary>
internal sealed class PartnerDirectory(PartnerDbContext db, ITenantContext tenantContext) : IPartnerDirectory
{
    public async Task<PartnerInfo?> FindAsync(int partnerId, CancellationToken cancellationToken = default) =>
        (await FindManyAsync([partnerId], cancellationToken)).GetValueOrDefault(partnerId);

    public async Task<IReadOnlyDictionary<int, PartnerInfo>> FindManyAsync(IEnumerable<int> partnerIds, CancellationToken cancellationToken = default)
    {
        var ids = partnerIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<int, PartnerInfo>();

        var tenant = tenantContext.TenantId;
        var partners = await db.Partners.AsNoTracking()
            .Where(p => p.TenantId == tenant && !p.IsDeleted && ids.Contains(p.BusinessPartnerId))
            .Select(p => new { p.BusinessPartnerId, p.BpCode, p.LegalName, p.DisplayName, p.Status })
            .ToListAsync(cancellationToken);
        var roles = (await db.Roles.AsNoTracking()
                .Where(r => r.TenantId == tenant && r.IsActive && ids.Contains(r.BusinessPartnerId))
                .Select(r => new { r.BusinessPartnerId, r.RoleCode }).ToListAsync(cancellationToken))
            .ToLookup(r => r.BusinessPartnerId, r => r.RoleCode);

        return partners.ToDictionary(
            p => p.BusinessPartnerId,
            p => new PartnerInfo(p.BusinessPartnerId, p.BpCode, p.LegalName, p.DisplayName ?? p.LegalName, p.Status, roles[p.BusinessPartnerId].Order().ToList()));
    }

    public async Task<IReadOnlyList<PartnerInfo>> AllAsync(CancellationToken cancellationToken = default)
    {
        var tenant = tenantContext.TenantId;
        var ids = await db.Partners.AsNoTracking().Where(p => p.TenantId == tenant && !p.IsDeleted).Select(p => p.BusinessPartnerId).ToListAsync(cancellationToken);
        return (await FindManyAsync(ids, cancellationToken)).Values.ToList();
    }
}
