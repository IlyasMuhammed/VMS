using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using VMS.Modules.Notifications.Data;
using VMS.Modules.Notifications.Domain;
using VMS.Modules.Notifications.Models;
using VMS.Shared.Common;
using VMS.Shared.Exceptions;
using VMS.Shared.Messages;
using VMS.Shared.Notifications;

namespace VMS.Modules.Notifications.Services;

public interface INotificationRuleService
{
    /// <summary>The tenant's notification rules, seeding the six event types' defaults the first time they are asked for (S0-FND-12's lazy-seed idiom).</summary>
    Task<List<NotificationRuleModel>> ListAsync();
    Task<NotificationRuleModel> UpdateAsync(int id, SaveNotificationRuleRequest request);
}

/// <summary>The notification rule master (FSD §9, §19A.6, §23.4): one row per event type, admin-configurable. Rows are never added or removed by hand — the six event types are fixed in code (BR-NOT-001) — only their lead days and recipients are edited.</summary>
internal sealed class NotificationRuleService(NotificationDbContext db, ITenantContext tenantContext, IMessageCatalogue messages) : INotificationRuleService
{
    private Guid Tenant => tenantContext.TenantId;

    public async Task<List<NotificationRuleModel>> ListAsync()
    {
        await EnsureDefaultsAsync();
        var rows = await db.Rules.AsNoTracking().Where(r => r.TenantId == Tenant).OrderBy(r => r.EventType).ToListAsync();
        return rows.Select(ToModel).ToList();
    }

    private static NotificationRuleModel ToModel(NotificationRule r) => new()
    {
        Id = r.NotificationRuleId, EventType = r.EventType, Name = r.Name, LeadDays = r.LeadDays, RecipientPermission = r.RecipientPermission,
        EscalationAfterDays = r.EscalationAfterDays, EscalationPermission = r.EscalationPermission, IsActive = r.IsActive,
    };

    public async Task<NotificationRuleModel> UpdateAsync(int id, SaveNotificationRuleRequest request)
    {
        await EnsureDefaultsAsync();
        var rule = await db.Rules.FirstOrDefaultAsync(r => r.TenantId == Tenant && r.NotificationRuleId == id) ?? throw new NotFoundException("Notification rule not found.");

        var errors = new List<ValidationError>();
        void Add(string field, string code, params (string Name, object? Value)[] values) => errors.Add(messages.Error(field, code, values));

        if (request.LeadDays is < 0 or > 365) Add("leadDays", Msg.Invalid, ("Field", "Lead days"));
        if (string.IsNullOrWhiteSpace(request.RecipientPermission)) Add("recipientPermission", Msg.Required, ("Field", "Recipient"));

        var isOverdue = rule.EventType == NotificationEventTypes.ChargeOverdue;
        if (isOverdue)
        {
            if (request.EscalationAfterDays is not (> 0 and <= 90)) Add("escalationAfterDays", Msg.Invalid, ("Field", "Escalation after days"));
            if (string.IsNullOrWhiteSpace(request.EscalationPermission)) Add("escalationPermission", Msg.Required, ("Field", "Escalation recipient"));
        }
        if (errors.Count > 0) throw new ValidationException(errors);

        rule.LeadDays = request.LeadDays!.Value;
        rule.RecipientPermission = request.RecipientPermission!.Trim();
        rule.EscalationAfterDays = isOverdue ? request.EscalationAfterDays : null;
        rule.EscalationPermission = isOverdue ? request.EscalationPermission?.Trim() : null;
        rule.IsActive = request.IsActive;
        await db.SaveChangesAsync();
        return ToModel(rule);
    }

    // ── Starting a tenant's rules from the defaults (S0-FND-12's idiom) ─────────────

    private async Task EnsureDefaultsAsync()
    {
        var tenant = Tenant;
        if (tenant == Guid.Empty) return;

        var connection = db.Database.GetDbConnection();
        var wasClosed = connection.State != System.Data.ConnectionState.Open;
        if (wasClosed) await connection.OpenAsync();
        try
        {
            var transaction = db.Database.CurrentTransaction?.GetDbTransaction();

            await using (var check = RawSql.Command(connection, transaction, "SELECT CASE WHEN EXISTS (SELECT 1 FROM notif.NotificationRules WHERE TenantId = @tenant) THEN 1 ELSE 0 END", ("@tenant", tenant)))
            {
                if (Convert.ToInt32(await check.ExecuteScalarAsync()) == 1) return;
            }

            var parameters = new List<(string, object?)> { ("@tenant", tenant) };
            var rows = new List<string>();
            for (var i = 0; i < NotificationRuleDefaults.Defaults.Count; i++)
            {
                var s = NotificationRuleDefaults.Defaults[i];
                parameters.Add(($"@event{i}", s.EventType));
                parameters.Add(($"@name{i}", s.Name));
                parameters.Add(($"@lead{i}", s.LeadDays));
                parameters.Add(($"@recip{i}", s.RecipientPermission));
                parameters.Add(($"@esc{i}", s.EscalationAfterDays));
                parameters.Add(($"@escrecip{i}", s.EscalationPermission));
                rows.Add($"(@tenant, @event{i}, @name{i}, @lead{i}, @recip{i}, @esc{i}, @escrecip{i}, 1)");
            }

            await using var insert = RawSql.Command(connection, transaction,
                $@"SET XACT_ABORT ON;
                   BEGIN TRAN;
                   IF NOT EXISTS (SELECT 1 FROM notif.NotificationRules WITH (UPDLOCK, HOLDLOCK) WHERE TenantId = @tenant)
                       INSERT INTO notif.NotificationRules (TenantId, EventType, Name, LeadDays, RecipientPermission, EscalationAfterDays, EscalationPermission, IsActive)
                       VALUES {string.Join(", ", rows)};
                   COMMIT;",
                parameters.ToArray());
            await insert.ExecuteNonQueryAsync();
        }
        finally
        {
            if (wasClosed) await connection.CloseAsync();
        }
    }
}
