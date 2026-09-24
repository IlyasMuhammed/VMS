using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VMS.Modules.Documents.Data;
using VMS.Modules.Documents.Domain;
using VMS.Shared.Common;
using VMS.Shared.Files;
using VMS.Shared.Time;

namespace VMS.Modules.Documents.Services;

public sealed record ExpiryRecalculationResult(int Recalculated, int BecameExpiringSoon, int BecameExpired);

public interface IDocumentExpiryRecalculator
{
    /// <summary>Recalculates Active / Expiring Soon / Expired against today, for the signed-in tenant (BR-DOC-003). A Superseded or Rejected version is never touched.</summary>
    Task<ExpiryRecalculationResult> RunAsync(CancellationToken cancellationToken = default);
}

internal sealed class DocumentExpiryRecalculator(DocumentDbContext db, ITenantContext tenantContext, IOperatingClock clock) : IDocumentExpiryRecalculator
{
    public async Task<ExpiryRecalculationResult> RunAsync(CancellationToken ct = default)
    {
        var tenant = tenantContext.TenantId;
        var today = await clock.TodayAsync(tenant);

        var types = await db.Types.AsNoTracking().Where(t => t.TenantId == tenant).ToDictionaryAsync(t => t.DocumentTypeId, t => t.RenewalLeadDays, ct);
        var live = await db.Documents.Where(d => d.TenantId == tenant && DocumentStatuses.Live.Contains(d.Status) && d.IsCurrent && d.ExpiryDate != null).ToListAsync(ct);

        var recalculated = 0; var expiringSoon = 0; var expired = 0;
        foreach (var doc in live)
        {
            var leadDays = types.GetValueOrDefault(doc.DocumentTypeId, 30);
            var next = doc.ExpiryDate!.Value < today ? DocumentStatuses.Expired
                : doc.ExpiryDate.Value <= today.AddDays(leadDays) ? DocumentStatuses.ExpiringSoon
                : DocumentStatuses.Active;
            if (next == doc.Status) continue;
            doc.Status = next;
            recalculated++;
            if (next == DocumentStatuses.ExpiringSoon) expiringSoon++;
            else if (next == DocumentStatuses.Expired) expired++;
        }
        if (recalculated > 0) await db.SaveChangesAsync(ct);
        return new ExpiryRecalculationResult(recalculated, expiringSoon, expired);
    }
}

public sealed record RetentionResult(int Removed);

public interface IDocumentRetentionCleaner
{
    /// <summary>Removes a Superseded or Rejected version once its type's retention period has passed since it stopped being current (BR-DOC-002), and logs what it removed.</summary>
    Task<RetentionResult> RunAsync(CancellationToken cancellationToken = default);
}

internal sealed class DocumentRetentionCleaner(DocumentDbContext db, ITenantContext tenantContext, IOperatingClock clock, IFileStore fileStore, ILogger<DocumentRetentionCleaner> logger) : IDocumentRetentionCleaner
{
    public async Task<RetentionResult> RunAsync(CancellationToken ct = default)
    {
        var tenant = tenantContext.TenantId;
        var today = await clock.TodayAsync(tenant);

        var types = await db.Types.AsNoTracking().Where(t => t.TenantId == tenant).ToDictionaryAsync(t => t.DocumentTypeId, t => t.RetentionYears, ct);
        var candidates = await db.Documents.Where(d => d.TenantId == tenant && !d.IsCurrent && (d.Status == DocumentStatuses.Superseded || d.Status == DocumentStatuses.Rejected)).ToListAsync(ct);

        var removed = 0;
        foreach (var doc in candidates)
        {
            var years = types.GetValueOrDefault(doc.DocumentTypeId, 7);
            // Aged from when the version was made, not from its (possibly long-expired) expiry date: BR-DOC-002 keeps history for a fixed span, not until the paper itself expired.
            if (doc.CreatedOn.AddYears(years) > DateTime.UtcNow) continue;

            try { await fileStore.DeleteAsync(tenant, doc.StorageKey); } catch (Exception ex) { logger.LogWarning(ex, "Could not delete the file behind document {DocumentCode}; the row is kept so the job can try again.", doc.DocumentCode); continue; }
            db.Documents.Remove(doc);
            removed++;
            logger.LogInformation("Retention removed {DocumentCode} ({OwnerType} #{OwnerId}, version {VersionNo}), past its {Years}-year retention.", doc.DocumentCode, doc.OwnerType, doc.OwnerId, doc.VersionNo, years);
        }
        if (removed > 0) await db.SaveChangesAsync(ct);
        return new RetentionResult(removed);
    }
}
