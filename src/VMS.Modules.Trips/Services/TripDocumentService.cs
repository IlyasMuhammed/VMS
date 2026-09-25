using Microsoft.EntityFrameworkCore;
using VMS.Modules.Trips.Data;
using VMS.Modules.Trips.Domain;
using VMS.Modules.Trips.Models;
using VMS.Shared.Authorization;
using VMS.Shared.Common;
using VMS.Shared.Exceptions;
using VMS.Shared.Files;
using VMS.Shared.Messages;

namespace VMS.Modules.Trips.Services;

/// <summary>Trip documents that are not a POD (§23): loading slip, gate pass, challan, photo, other.</summary>
public interface ITripDocumentService
{
    Task<IReadOnlyList<TripDocumentModel>> ListAsync(long tripId, TripCaller caller, CancellationToken ct = default);
    Task<TripDocumentModel> UploadAsync(long tripId, string documentType, Stream content, string fileName, TripCaller caller, CancellationToken ct = default);
    Task<TripDownloadLinkModel> DownloadLinkAsync(long tripDocumentId, CancellationToken ct = default);
}

internal sealed class TripDocumentService(TripsDbContext db, ITenantContext tenant, ICallerScope scope, IFileStore files, IFileDownloadLinks links, IMessageCatalogue messages)
    : ITripDocumentService
{
    public async Task<IReadOnlyList<TripDocumentModel>> ListAsync(long tripId, TripCaller caller, CancellationToken ct = default)
    {
        var trip = await FindTripAsync(tripId, ct);
        TripAccess.RequireViewOrOwnDriver(trip, caller, scope, "Viewing this trip's documents");
        var docs = await db.TripDocuments.AsNoTracking().Where(d => d.TenantId == tenant.TenantId && d.TripId == tripId)
            .OrderByDescending(d => d.UploadedAtUtc).ToListAsync(ct);
        return docs.Select(ToModel).ToList();
    }

    public async Task<TripDocumentModel> UploadAsync(long tripId, string documentType, Stream content, string fileName, TripCaller caller, CancellationToken ct = default)
    {
        var trip = await FindTripAsync(tripId, ct);
        TripAccess.RequireDocumentsOrOwnDriver(trip, caller, scope, "Uploading a trip document");
        if (!TripDocumentTypes.All.Contains(documentType))
            throw new ValidationException(messages.Error("documentType", Msg.OneOf, ("Field", "Document type"), ("Allowed", string.Join(", ", TripDocumentTypes.All))));

        var stored = await files.SaveAsync(new FileUpload(content, fileName, new FileOwner("TripDocument", tripId.ToString()), FileRules.Scans, tenant.TenantId), ct);
        var doc = new TripDocument
        {
            TripId = tripId, DocumentType = documentType, StorageKey = stored.StorageKey, Sha256 = stored.Sha256, SizeBytes = stored.SizeBytes,
            ContentType = stored.ContentType, OriginalFileName = stored.OriginalFileName, UploadedBy = caller.UserId, UploadedAtUtc = DateTime.UtcNow,
            Source = TripAccess.IsOwnDriver(trip, scope) ? TripEventSources.DriverApp : TripEventSources.Manual
        };
        db.TripDocuments.Add(doc);
        await db.SaveChangesAsync(ct);
        return ToModel(doc);
    }

    public async Task<TripDownloadLinkModel> DownloadLinkAsync(long tripDocumentId, CancellationToken ct = default)
    {
        var doc = await db.TripDocuments.AsNoTracking().FirstOrDefaultAsync(d => d.TenantId == tenant.TenantId && d.TripDocumentId == tripDocumentId, ct)
            ?? throw new NotFoundException($"Trip document {tripDocumentId} was not found.");
        var stored = new StoredFile(doc.StorageKey, doc.Sha256, doc.SizeBytes, doc.ContentType, doc.OriginalFileName, doc.UploadedAtUtc);
        var link = links.Create(tenant.TenantId, stored, inline: true);
        return new TripDownloadLinkModel { Url = link.Url, ExpiresAtUtc = link.ExpiresAtUtc };
    }

    private async Task<Trip> FindTripAsync(long tripId, CancellationToken ct) =>
        await db.Trips.AsNoTracking().FirstOrDefaultAsync(t => t.TenantId == tenant.TenantId && t.TripId == tripId, ct)
        ?? throw new NotFoundException($"Trip {tripId} was not found.");

    private static TripDocumentModel ToModel(TripDocument d) => new()
    {
        TripDocumentId = d.TripDocumentId, TripId = d.TripId, DocumentType = d.DocumentType, OriginalFileName = d.OriginalFileName,
        SizeBytes = d.SizeBytes, UploadedBy = d.UploadedBy, UploadedAtUtc = d.UploadedAtUtc, Source = d.Source
    };
}
