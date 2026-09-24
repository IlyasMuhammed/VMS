using Microsoft.EntityFrameworkCore;
using VMS.Modules.Documents.Data;
using VMS.Modules.Documents.Domain;
using VMS.Shared.Common;
using VMS.Shared.Notifications;
using VMS.Shared.Partners;
using VMS.Shared.Vehicles;

namespace VMS.Modules.Documents.Services;

/// <summary>
/// Answers the Notifications module's "what is due" question for documents (FSD §23.4's four DocumentExpiry rows —
/// insurance, fitness certificate, route permit, token tax — and every other expirable type besides): every current
/// version with an expiry date, still Active or Expiring Soon (an already-Expired one no longer needs a heads-up, it needs
/// action, and stays visible on the Missing/Register reports instead). One candidate per live document; the evaluator
/// applies the rule and works out who to tell.
/// </summary>
internal sealed class DocumentNotificationSource(DocumentDbContext db, ITenantContext tenantContext, IVehicleDirectory vehicles, IPartnerDirectory partners) : IDocumentNotificationSource
{
    public async Task<IReadOnlyList<NotificationCandidate>> FindDueAsync(CancellationToken ct = default)
    {
        var tenant = tenantContext.TenantId;

        var live = await db.Documents.AsNoTracking()
            .Where(d => d.TenantId == tenant && d.IsCurrent && d.ExpiryDate != null
                     && (d.Status == DocumentStatuses.Active || d.Status == DocumentStatuses.ExpiringSoon))
            .ToListAsync(ct);
        if (live.Count == 0) return [];

        var typeNames = await db.Types.AsNoTracking().Where(t => t.TenantId == tenant).ToDictionaryAsync(t => t.DocumentTypeId, t => t.Name, ct);

        var vehicleIds = live.Where(d => d.OwnerType == DocumentOwnerTypes.Vehicle).Select(d => d.OwnerId).Distinct().ToList();
        var partnerIds = live.Where(d => d.OwnerType == DocumentOwnerTypes.BusinessPartner).Select(d => d.OwnerId).Distinct().ToList();
        var v = vehicleIds.Count > 0 ? await vehicles.FindManyAsync(vehicleIds, ct) : new Dictionary<int, VehicleInfo>();
        var p = partnerIds.Count > 0 ? await partners.FindManyAsync(partnerIds, ct) : new Dictionary<int, PartnerInfo>();

        var result = new List<NotificationCandidate>(live.Count);
        foreach (var d in live)
        {
            var ownerName = d.OwnerType == DocumentOwnerTypes.Vehicle
                ? v.GetValueOrDefault(d.OwnerId)?.RegistrationNo ?? $"#{d.OwnerId}"
                : p.GetValueOrDefault(d.OwnerId)?.DisplayName ?? $"#{d.OwnerId}";
            var typeName = typeNames.GetValueOrDefault(d.DocumentTypeId, "Document");

            result.Add(new NotificationCandidate(
                NotificationEventTypes.DocumentExpiry, nameof(Document), d.DocumentId.ToString(),
                d.OwnerType, d.OwnerId, ownerName, d.ExpiryDate!.Value,
                $"{typeName} for {ownerName} expires on {d.ExpiryDate:yyyy-MM-dd}."));
        }
        return result;
    }
}
