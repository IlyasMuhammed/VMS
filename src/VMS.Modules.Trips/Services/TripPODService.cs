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

/// <summary>Proof of delivery upload and approval (§23/§25: PODStatus Uploaded/Approved/Rejected).</summary>
public interface ITripPODService
{
    Task<IReadOnlyList<TripPODModel>> ListAsync(long tripId, TripCaller caller, CancellationToken ct = default);
    Task<TripPODModel> UploadAsync(long tripId, Stream content, string fileName, TripCaller caller, CancellationToken ct = default);
    Task<TripPODModel> ApproveAsync(long tripPodId, int approvedByUserId, CancellationToken ct = default);
    Task<TripPODModel> RejectAsync(long tripPodId, RejectPodRequest request, int rejectedByUserId, CancellationToken ct = default);
    Task<TripDownloadLinkModel> DownloadLinkAsync(long tripPodId, CancellationToken ct = default);
}

internal sealed class TripPODService(TripsDbContext db, ITenantContext tenant, ICallerScope scope, IFileStore files, IFileDownloadLinks links, IMessageCatalogue messages, ITripEventRecorder events)
    : ITripPODService
{
    public async Task<IReadOnlyList<TripPODModel>> ListAsync(long tripId, TripCaller caller, CancellationToken ct = default)
    {
        var trip = await FindTripAsync(tripId, ct);
        TripAccess.RequireViewOrOwnDriver(trip, caller, scope, "Viewing this trip's proof of delivery");
        var pods = await db.TripPODs.AsNoTracking().Where(p => p.TenantId == tenant.TenantId && p.TripId == tripId)
            .OrderByDescending(p => p.UploadedAtUtc).ToListAsync(ct);
        return pods.Select(ToModel).ToList();
    }

    public async Task<TripPODModel> UploadAsync(long tripId, Stream content, string fileName, TripCaller caller, CancellationToken ct = default)
    {
        var trip = await FindTripAsync(tripId, ct);
        TripAccess.RequireDocumentsOrOwnDriver(trip, caller, scope, "Uploading a proof of delivery");

        var stored = await files.SaveAsync(new FileUpload(content, fileName, new FileOwner("TripPOD", tripId.ToString()), FileRules.Scans, tenant.TenantId), ct);
        var source = TripAccess.IsOwnDriver(trip, scope) ? TripEventSources.DriverApp : TripEventSources.Manual;
        var pod = new TripPOD
        {
            TripId = tripId, StorageKey = stored.StorageKey, Sha256 = stored.Sha256, SizeBytes = stored.SizeBytes,
            ContentType = stored.ContentType, OriginalFileName = stored.OriginalFileName, UploadedBy = caller.UserId,
            UploadedAtUtc = DateTime.UtcNow, Source = source, Status = PodStatuses.Uploaded
        };
        db.TripPODs.Add(pod);
        events.Record(tripId, TripEventTypes.PODUploaded, source, caller.UserId);
        await db.SaveChangesAsync(ct);
        return ToModel(pod);
    }

    public async Task<TripPODModel> ApproveAsync(long tripPodId, int approvedByUserId, CancellationToken ct = default)
    {
        var pod = await FindPodAsync(tripPodId, ct);
        if (pod.Status != PodStatuses.Uploaded) throw new ConflictException($"This proof of delivery is already {pod.Status}.");
        pod.Status = PodStatuses.Approved;
        pod.ApprovedBy = approvedByUserId;
        pod.ApprovedAtUtc = DateTime.UtcNow;
        pod.RejectedReason = null;
        await db.SaveChangesAsync(ct);
        return ToModel(pod);
    }

    public async Task<TripPODModel> RejectAsync(long tripPodId, RejectPodRequest request, int rejectedByUserId, CancellationToken ct = default)
    {
        var pod = await FindPodAsync(tripPodId, ct);
        if (pod.Status != PodStatuses.Uploaded) throw new ConflictException($"This proof of delivery is already {pod.Status}.");
        if (string.IsNullOrWhiteSpace(request.Reason)) throw new ValidationException(messages.Error("reason", Msg.Required, ("Field", "Rejection reason")));
        pod.Status = PodStatuses.Rejected;
        pod.ApprovedBy = rejectedByUserId;
        pod.ApprovedAtUtc = DateTime.UtcNow;
        pod.RejectedReason = request.Reason.Trim();
        await db.SaveChangesAsync(ct);
        return ToModel(pod);
    }

    public async Task<TripDownloadLinkModel> DownloadLinkAsync(long tripPodId, CancellationToken ct = default)
    {
        var pod = await FindPodAsync(tripPodId, ct);
        var stored = new StoredFile(pod.StorageKey, pod.Sha256, pod.SizeBytes, pod.ContentType, pod.OriginalFileName, pod.UploadedAtUtc);
        var link = links.Create(tenant.TenantId, stored, inline: true);
        return new TripDownloadLinkModel { Url = link.Url, ExpiresAtUtc = link.ExpiresAtUtc };
    }

    private async Task<Trip> FindTripAsync(long tripId, CancellationToken ct) =>
        await db.Trips.AsNoTracking().FirstOrDefaultAsync(t => t.TenantId == tenant.TenantId && t.TripId == tripId, ct)
        ?? throw new NotFoundException($"Trip {tripId} was not found.");

    private async Task<TripPOD> FindPodAsync(long tripPodId, CancellationToken ct) =>
        await db.TripPODs.FirstOrDefaultAsync(p => p.TenantId == tenant.TenantId && p.TripPODId == tripPodId, ct)
        ?? throw new NotFoundException($"Proof of delivery {tripPodId} was not found.");

    private static TripPODModel ToModel(TripPOD p) => new()
    {
        TripPODId = p.TripPODId, TripId = p.TripId, OriginalFileName = p.OriginalFileName, SizeBytes = p.SizeBytes, UploadedBy = p.UploadedBy,
        UploadedAtUtc = p.UploadedAtUtc, Source = p.Source, Status = p.Status, ApprovedBy = p.ApprovedBy, ApprovedAtUtc = p.ApprovedAtUtc, RejectedReason = p.RejectedReason
    };
}
