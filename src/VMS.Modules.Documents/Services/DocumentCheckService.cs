using Microsoft.EntityFrameworkCore;
using VMS.Modules.Documents.Data;
using VMS.Modules.Documents.Domain;
using VMS.Shared.Common;
using VMS.Shared.Documents;
using VMS.Shared.Vehicles;

namespace VMS.Modules.Documents.Services;

/// <summary>
/// What other modules ask about a record's documents (BR-VH-015, BR-BP-005, BR-DOC-004): the real answer, replacing the
/// no-op stand-ins those modules shipped with before this module existed (<c>NoDocumentCheck</c>).
/// </summary>
internal sealed class DocumentCheckService(DocumentDbContext db, ITenantContext tenantContext, IDocumentTypeService typeService) : IDocumentCheck, IVehicleDocumentCheck
{
    private Guid Tenant => tenantContext.TenantId;

    public bool CanCheck => true;

    public async Task<IReadOnlyList<MissingDocument>> MissingAsync(string ownerType, int ownerId, IReadOnlySet<string> partnerRoles, CancellationToken cancellationToken = default)
    {
        await typeService.ListAsync();   // the type master seeds lazily (S0-FND-12's idiom); make sure it exists before asking what is missing from it
        var types = await db.Types.AsNoTracking()
            .Where(t => t.TenantId == Tenant && t.IsActive && t.MandatoryLevel != MandatoryLevels.None
                        && (t.AppliesTo & (ownerType == DocumentOwnerTypes.Vehicle ? AppliesTo.Vehicle : AppliesTo.BusinessPartner)) != 0)
            .ToListAsync(cancellationToken);
        var applicable = types.Where(t => t.PartnerRole is null || partnerRoles.Contains(t.PartnerRole)).ToList();
        if (applicable.Count == 0) return [];

        var current = await db.Documents.AsNoTracking()
            .Where(d => d.TenantId == Tenant && d.OwnerType == ownerType && d.OwnerId == ownerId && d.IsCurrent)
            .Select(d => d.DocumentTypeId).ToListAsync(cancellationToken);

        return applicable.Where(t => !current.Contains(t.DocumentTypeId))
            .Select(t => new MissingDocument(t.Code, t.Name, t.MandatoryLevel == MandatoryLevels.Required))
            .ToList();
    }

    /// <summary>BR-VH-015: a Self Owned or Bank Leased vehicle needs its registration book. Asked by activation specifically, so it stays its own narrow method rather than going through the generic list above.</summary>
    public async Task<bool> HasRegistrationBookAsync(int vehicleId, CancellationToken cancellationToken = default)
    {
        await typeService.ListAsync();   // ensure the type master is seeded first — see MissingAsync
        var type = await db.Types.AsNoTracking().FirstOrDefaultAsync(t => t.TenantId == Tenant && t.Code == PlatformDocumentTypes.RegistrationBook, cancellationToken);
        if (type is null) return false;
        return await db.Documents.AsNoTracking().AnyAsync(d => d.TenantId == Tenant && d.OwnerType == DocumentOwnerTypes.Vehicle && d.OwnerId == vehicleId
            && d.DocumentTypeId == type.DocumentTypeId && d.IsCurrent, cancellationToken);
    }
}
