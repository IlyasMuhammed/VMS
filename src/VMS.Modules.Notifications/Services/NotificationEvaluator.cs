using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using VMS.Modules.Notifications.Data;
using VMS.Modules.Notifications.Domain;
using VMS.Shared.Common;
using VMS.Shared.Notifications;
using VMS.Shared.Time;
using VMS.Shared.Users;

namespace VMS.Modules.Notifications.Services;

public sealed record NotificationEvaluationResult(int Created);

/// <summary>
/// The notification engine (FSD §19A.6, §23.4, NFR-DT-06): pulls candidates from every source module, applies each
/// event type's rule, resolves recipients, and writes one row per (recipient, source record, event) that has not
/// already been told (BR-NOT-002). Runs both from the hourly background job and, narrowed to one candidate, from a
/// save-time hook (S7-NOT-02) — the same method either way, so "immediately" and "on the next run" can never disagree.
/// No separate Subscription table: a candidate is recomputed fresh from the live source data every time (the same idiom
/// <c>DocumentExpiryRecalculator</c> and <c>RecurringChargeGenerator</c> already use), so a renewed document or a paid
/// charge simply stops producing one, with nothing to clean up.
/// </summary>
public interface INotificationEvaluator
{
    /// <summary>Evaluates every candidate from every source against the tenant's rules and writes what is newly due.</summary>
    Task<NotificationEvaluationResult> RunAsync(CancellationToken cancellationToken = default);

    /// <summary>Evaluates only the candidates that share this one source record (S7-NOT-02's save-time hook) — a document just uploaded, a charge just configured, an installment schedule just generated — so it is told about immediately, not at the next hourly run.</summary>
    Task<NotificationEvaluationResult> RunForSourceAsync(string sourceEntity, string sourceId, CancellationToken cancellationToken = default);
}

internal sealed class NotificationEvaluator(
    NotificationDbContext db, ITenantContext tenantContext, IOperatingClock clock, IUserDirectory users,
    INotificationRuleService ruleService, IDocumentNotificationSource documents, IVehicleNotificationSource vehicles)
    : INotificationEvaluator
{
    public async Task<NotificationEvaluationResult> RunAsync(CancellationToken ct = default)
    {
        var candidates = (await documents.FindDueAsync(ct)).Concat(await vehicles.FindDueAsync(ct)).ToList();
        return await EvaluateAsync(candidates, ct);
    }

    public async Task<NotificationEvaluationResult> RunForSourceAsync(string sourceEntity, string sourceId, CancellationToken ct = default)
    {
        var candidates = (await documents.FindDueAsync(ct)).Concat(await vehicles.FindDueAsync(ct))
            .Where(c => c.SourceEntity == sourceEntity && c.SourceId == sourceId).ToList();
        return await EvaluateAsync(candidates, ct);
    }

    private async Task<NotificationEvaluationResult> EvaluateAsync(List<NotificationCandidate> candidates, CancellationToken ct)
    {
        var tenant = tenantContext.TenantId;
        if (candidates.Count == 0) return new NotificationEvaluationResult(0);

        var today = await clock.TodayAsync(tenant);
        var rules = (await ruleService.ListAsync()).Where(r => r.IsActive).ToDictionary(r => r.EventType);

        // Every existing notification for the source rows in play, so a recipient already told is never told twice
        // for the same record and event (BR-NOT-002) — narrowed to just these keys, not the whole history.
        var entities = candidates.Select(c => c.SourceEntity).Distinct().ToList();
        var ids = candidates.Select(c => c.SourceId).Distinct().ToList();
        var existing = await db.Notifications.AsNoTracking()
            .Where(n => n.TenantId == tenant && entities.Contains(n.SourceEntity) && ids.Contains(n.SourceId))
            .Select(n => new { n.UserId, n.SourceEntity, n.SourceId, n.EventType, n.IsEscalation, n.OccurredOn })
            .ToListAsync(ct);
        var lastNotified = existing
            .GroupBy(n => (n.UserId, n.SourceEntity, n.SourceId, n.EventType, n.IsEscalation))
            .ToDictionary(g => g.Key, g => g.Max(n => n.OccurredOn));

        var toAdd = new List<Notification>();
        var recipientCache = new Dictionary<string, IReadOnlyList<UserInfo>>();
        async Task<IReadOnlyList<UserInfo>> RecipientsAsync(string permission)
        {
            if (!recipientCache.TryGetValue(permission, out var found))
                recipientCache[permission] = found = await users.FindActiveByPermissionAsync(permission, ct);
            return found;
        }

        foreach (var candidate in candidates)
        {
            if (!rules.TryGetValue(candidate.EventType, out var rule)) continue;

            var isOverdue = candidate.EventType == NotificationEventTypes.ChargeOverdue;
            var due = isOverdue
                ? candidate.TriggerDate <= today   // already overdue by definition (the source only offers Overdue-status entries)
                : candidate.TriggerDate <= today.AddDays(rule.LeadDays);
            if (!due) continue;

            var title = rule.Name;
            var occurredOn = DateTime.UtcNow;

            foreach (var recipient in await RecipientsAsync(rule.RecipientPermission))
                QueueIfNew(recipient.UserId, escalated: false);

            // BR-NOT-002's overdue exception: re-notify (and add the escalation recipient) once EscalationAfterDays has passed since the last notice, instead of only once.
            if (isOverdue && rule.EscalationAfterDays is { } after && rule.EscalationPermission is { Length: > 0 } escPermission)
            {
                var overdueDays = today.DayNumber - candidate.TriggerDate.DayNumber;
                if (overdueDays >= after)
                    foreach (var recipient in await RecipientsAsync(escPermission))
                        QueueIfNew(recipient.UserId, escalated: true);
            }

            void QueueIfNew(int userId, bool escalated)
            {
                // Escalation is its own dedup lane (BR-NOT-002): the escalation recipient can be the very same
                // person as the ordinary recipient, and must still get both — an escalation is additional, not a substitute.
                var key = (userId, candidate.SourceEntity, candidate.SourceId, candidate.EventType, escalated);
                var already = lastNotified.TryGetValue(key, out var last);
                if (already)
                {
                    if (!isOverdue) return;   // one heads-up is enough for every event type but ChargeOverdue
                    var daysSince = today.DayNumber - DateOnly.FromDateTime(last).DayNumber;
                    if (daysSince < (rule.EscalationAfterDays ?? int.MaxValue)) return;
                }
                // Also skip a duplicate within this very run (two rules could never collide, but a repeat candidate could if a source ever returned one twice).
                if (toAdd.Any(n => n.UserId == userId && n.SourceEntity == candidate.SourceEntity && n.SourceId == candidate.SourceId && n.EventType == candidate.EventType && n.IsEscalation == escalated)) return;

                toAdd.Add(new Notification
                {
                    UserId = userId, EventType = candidate.EventType, Title = escalated ? $"{title} (escalated)" : title, Message = candidate.Summary,
                    SourceEntity = candidate.SourceEntity, SourceId = candidate.SourceId, OwnerType = candidate.OwnerType, OwnerId = candidate.OwnerId,
                    IsEscalation = escalated, OccurredOn = occurredOn, IsRead = false,
                });
                lastNotified[key] = occurredOn;
            }
        }

        // Saved one at a time, not as a batch: the hourly job and an admin's "run now" (or two tenants' loop
        // iterations) can race and both decide the same notification is new, exactly as BR-VH-030 protects a
        // recurring charge entry's period key. The real unique index is what actually prevents the duplicate; this
        // catch is only what turns that into "nothing more to do" instead of a failed request.
        var created = 0;
        foreach (var notification in toAdd)
        {
            db.Notifications.Add(notification);
            try { await db.SaveChangesAsync(ct); created++; }
            catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
            {
                db.Entry(notification).State = EntityState.Detached;   // another run already wrote this same notification a moment ago
            }
        }
        return new NotificationEvaluationResult(created);
    }
}
