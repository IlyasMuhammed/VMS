using Microsoft.Extensions.Logging;
using VMS.Shared.Notifications;

namespace VMS.Modules.Notifications.Services;

/// <summary>The real answer to a source module's save-time "tell the evaluator about this one row now" call (FR-BP-016, FR-VH-008), backed by the same evaluator the hourly job uses.</summary>
internal sealed class NotificationTrigger(INotificationEvaluator evaluator, ILogger<NotificationTrigger> logger) : INotificationTrigger
{
    public async Task NotifyAsync(string sourceEntity, string sourceId, CancellationToken ct = default)
    {
        try { await evaluator.RunForSourceAsync(sourceEntity, sourceId, ct); }
        catch (Exception ex)
        {
            // A missed immediate notification is recovered by the hourly job regardless (S7-NOT-03) — never let this courtesy call fail the save that triggered it.
            logger.LogWarning(ex, "Immediate notification evaluation failed for {SourceEntity} #{SourceId}; the hourly job will still pick it up.", sourceEntity, sourceId);
        }
    }
}
