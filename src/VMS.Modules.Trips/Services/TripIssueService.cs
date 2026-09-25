using Microsoft.EntityFrameworkCore;
using VMS.Modules.Trips.Data;
using VMS.Modules.Trips.Domain;
using VMS.Modules.Trips.Models;
using VMS.Shared.Authorization;
using VMS.Shared.Common;
using VMS.Shared.Exceptions;
using VMS.Shared.Messages;

namespace VMS.Modules.Trips.Services;

/// <summary>Trip issues (§23): Breakdown, Accident, Delay, Customer Hold, Route Blocked, Other; an issue can
/// optionally put the trip On Hold.</summary>
public interface ITripIssueService
{
    Task<IReadOnlyList<TripIssueModel>> ListAsync(long tripId, TripCaller caller, CancellationToken ct = default);
    Task<TripIssueModel> CreateAsync(long tripId, CreateTripIssueRequest request, TripCaller caller, CancellationToken ct = default);
    Task<TripIssueModel> ResolveAsync(long tripIssueId, ResolveTripIssueRequest request, int resolvedByUserId, CancellationToken ct = default);
}

internal sealed class TripIssueService(TripsDbContext db, ITenantContext tenant, ICallerScope scope, IMessageCatalogue messages, ITripEventRecorder events, ITripLifecycleService lifecycle)
    : ITripIssueService
{
    public async Task<IReadOnlyList<TripIssueModel>> ListAsync(long tripId, TripCaller caller, CancellationToken ct = default)
    {
        var trip = await FindTripAsync(tripId, ct);
        TripAccess.RequireViewOrOwnDriver(trip, caller, scope, "Viewing this trip's issues");
        var issues = await db.TripIssues.AsNoTracking().Where(i => i.TenantId == tenant.TenantId && i.TripId == tripId)
            .OrderByDescending(i => i.ReportedAtUtc).ToListAsync(ct);
        return issues.Select(ToModel).ToList();
    }

    public async Task<TripIssueModel> CreateAsync(long tripId, CreateTripIssueRequest request, TripCaller caller, CancellationToken ct = default)
    {
        var trip = await FindTripAsync(tripId, ct);
        TripAccess.RequireStatusOrOwnDriver(trip, caller, scope, "Reporting a trip issue");

        if (!TripIssueTypes.All.Contains(request.IssueType))
            throw new ValidationException(messages.Error("issueType", Msg.OneOf, ("Field", "Issue type"), ("Allowed", string.Join(", ", TripIssueTypes.All))));
        if (!IssueSeverities.All.Contains(request.Severity))
            throw new ValidationException(messages.Error("severity", Msg.OneOf, ("Field", "Severity"), ("Allowed", string.Join(", ", IssueSeverities.All))));
        if (string.IsNullOrWhiteSpace(request.Description))
            throw new ValidationException(messages.Error("description", Msg.Required, ("Field", "Description")));
        if (request.PhotoDocumentId is { } photoId &&
            !await db.TripDocuments.AnyAsync(d => d.TenantId == tenant.TenantId && d.TripId == tripId && d.TripDocumentId == photoId, ct))
            throw new ValidationException(messages.Error("photoDocumentId", Msg.Invalid, ("Field", "Photo")));

        var issue = new TripIssue
        {
            TripId = tripId, IssueType = request.IssueType, Severity = request.Severity, Description = request.Description.Trim(),
            PhotoDocumentId = request.PhotoDocumentId, ReportedBy = caller.UserId, ReportedAtUtc = DateTime.UtcNow
        };
        db.TripIssues.Add(issue);
        events.Record(tripId, TripEventTypes.Issue, TripAccess.IsOwnDriver(trip, scope) ? TripEventSources.DriverApp : TripEventSources.Manual,
            caller.UserId, remarks: $"{request.IssueType}: {issue.Description}");
        await db.SaveChangesAsync(ct);

        if (request.PutOnHold)
        {
            // Reuses the lifecycle service's own Hold rules (eligible-from-status, permission, row version) rather
            // than a second, parallel hold path — the same "one implementation" discipline CC-12/13 already used
            // for trip-creation validation.
            await lifecycle.HoldAsync(tripId, new HoldTripRequest { Reason = $"Issue: {issue.Description}", RowVersion = Convert.ToBase64String(trip.RowVersion) }, caller, ct);
        }

        return ToModel(issue);
    }

    public async Task<TripIssueModel> ResolveAsync(long tripIssueId, ResolveTripIssueRequest request, int resolvedByUserId, CancellationToken ct = default)
    {
        var issue = await db.TripIssues.FirstOrDefaultAsync(i => i.TenantId == tenant.TenantId && i.TripIssueId == tripIssueId, ct)
            ?? throw new NotFoundException($"Trip issue {tripIssueId} was not found.");
        if (issue.IsResolved) throw new ConflictException("This issue is already resolved.");
        issue.IsResolved = true;
        issue.ResolvedBy = resolvedByUserId;
        issue.ResolvedAtUtc = DateTime.UtcNow;
        issue.ResolutionNotes = string.IsNullOrWhiteSpace(request.ResolutionNotes) ? null : request.ResolutionNotes.Trim();
        await db.SaveChangesAsync(ct);
        return ToModel(issue);
    }

    private async Task<Trip> FindTripAsync(long tripId, CancellationToken ct) =>
        await db.Trips.FirstOrDefaultAsync(t => t.TenantId == tenant.TenantId && t.TripId == tripId, ct)
        ?? throw new NotFoundException($"Trip {tripId} was not found.");

    private static TripIssueModel ToModel(TripIssue i) => new()
    {
        TripIssueId = i.TripIssueId, TripId = i.TripId, IssueType = i.IssueType, Severity = i.Severity, Description = i.Description,
        PhotoDocumentId = i.PhotoDocumentId, ReportedBy = i.ReportedBy, ReportedAtUtc = i.ReportedAtUtc, IsResolved = i.IsResolved,
        ResolvedBy = i.ResolvedBy, ResolvedAtUtc = i.ResolvedAtUtc, ResolutionNotes = i.ResolutionNotes
    };
}
