using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using VMS.Shared.Authorization;
using VMS.Shared.Common;

namespace VMS.Shared.Auditing;

/// <summary>
/// Reads the change tracker just before a save and turns each change into audit rows. A created or
/// deleted record becomes one row holding a JSON snapshot; an update becomes one row per changed field
/// with its old and new value. Values a caller may not see (<see cref="FieldPermissionAttribute"/> on the
/// entity property) get rows of their own, tagged with the permission needed to read them.
/// </summary>
internal sealed class AuditCapture
{
    private readonly List<Captured> _captured = [];

    private sealed record Change(string Field, string? Old, string? New, string? RequiredPermission);

    private sealed class Captured(EntityEntry entry, string action, string? recordId, string? snapshot, List<Change> changes)
    {
        public EntityEntry Entry { get; } = entry;
        public string Action { get; } = action;
        public string? RecordId { get; set; } = recordId;
        public string? Snapshot { get; } = snapshot;
        public List<Change> Changes { get; } = changes;
    }

    public bool IsEmpty => _captured.Count == 0;

    /// <summary>True when a created record's key is only assigned by the database, so its audit row can be written only after the save.</summary>
    public bool NeedsKeysAfterSave { get; private set; }

    public static AuditCapture From(DbContext db)
    {
        var capture = new AuditCapture();

        foreach (var entry in db.ChangeTracker.Entries().ToList())
        {
            // Any value the database has yet to assign (an identity key, or a foreign key that points at
            // one) makes a created record's snapshot wrong until the first save has run. That holds even
            // when the record that owns the key is itself not audited.
            if (entry.State == EntityState.Added && entry.Properties.Any(p => p.IsTemporary))
                capture.NeedsKeysAfterSave = true;

            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)) continue;

            var type = entry.Metadata.ClrType;
            if (type == typeof(AuditEntry) || type.IsDefined(typeof(NotAuditedAttribute), inherit: true)) continue;

            capture.Add(entry);
        }

        return capture;
    }

    private void Add(EntityEntry entry)
    {
        var properties = entry.Properties.Where(IsAudited).ToList();
        var hasTemporaryKey = entry.State == EntityState.Added &&
                              entry.Properties.Any(p => p.Metadata.IsPrimaryKey() && p.IsTemporary);
        var recordId = hasTemporaryKey ? null : RecordIdOf(entry);

        switch (entry.State)
        {
            case EntityState.Added:
                // Nothing is read yet: the snapshot is taken in Build, once keys and foreign keys are final.
                _captured.Add(new Captured(entry, AuditActions.Created, recordId, null, []));
                break;
            case EntityState.Deleted:
            {
                var (snapshot, restricted) = Split(properties, p => p.OriginalValue, nonNullOnly: true);
                _captured.Add(new Captured(entry, AuditActions.Deleted, recordId, snapshot,
                    restricted.Select(r => new Change(r.Name, r.Value, null, r.Permission)).ToList()));
                break;
            }
            default:
            {
                var changes = properties
                    .Where(p => p.IsModified && !Equals(p.OriginalValue, p.CurrentValue))
                    .Select(p => new Change(p.Metadata.Name, Text(p.OriginalValue), Text(p.CurrentValue), PermissionOf(p)))
                    .ToList();
                if (changes.Count > 0)
                    _captured.Add(new Captured(entry, AuditActions.Updated, recordId, null, changes));
                break;
            }
        }
    }

    /// <summary>Turns what was captured, and any queued notes, into rows. Call after the business save when keys were pending.</summary>
    public List<AuditEntry> Build(IAuditContext audit, AuditActor actor, Guid fallbackTenantId, DateTime now)
    {
        var rows = new List<AuditEntry>();

        AuditEntry Row(Guid tenant, string entity, string recordId, string action, string? field, string? oldValue, string? newValue,
            string? reason, string? permission, AuditRoot? root = null) => new()
        {
            OccurredAt = now,
            TenantId = tenant,
            GroupId = audit.GroupId,
            Entity = entity,
            RecordId = recordId,
            RootEntity = root?.Entity,
            RootRecordId = root?.RecordId,
            Action = action,
            Field = field,
            OldValue = oldValue,
            NewValue = newValue,
            Reason = reason,
            RequiredPermission = permission,
            UserId = actor.UserId,
            UserName = actor.UserName,
            IpAddress = actor.IpAddress
        };

        foreach (var c in _captured)
        {
            var tenant = TenantOf(c.Entry, fallbackTenantId);
            var entity = c.Entry.Metadata.ClrType.Name;
            var recordId = c.RecordId ?? RecordIdOf(c.Entry);
            var root = (c.Entry.Entity as IAuditRooted)?.GetAuditRoot();

            if (c.Action == AuditActions.Created)
            {
                var (snapshot, restricted) = Split(c.Entry.Properties.Where(IsAudited), p => p.CurrentValue, nonNullOnly: true);
                rows.Add(Row(tenant, entity, recordId, c.Action, null, null, snapshot, null, null, root));
                foreach (var r in restricted)
                    rows.Add(Row(tenant, entity, recordId, c.Action, r.Name, null, r.Value, null, r.Permission, root));
            }
            else if (c.Action == AuditActions.Deleted)
                rows.Add(Row(tenant, entity, recordId, c.Action, null, c.Snapshot, null, null, null, root));

            foreach (var change in c.Changes)
                rows.Add(Row(tenant, entity, recordId, c.Action, change.Field, change.Old, change.New, null, change.RequiredPermission, root));
        }

        foreach (var n in audit.PendingNotes)
            rows.Add(Row(fallbackTenantId, n.Entity, n.RecordId, n.Action, n.Field, n.OldValue, n.NewValue, n.Reason, n.RequiredPermission,
                n.RootEntity is null || n.RootRecordId is null ? null : new AuditRoot(n.RootEntity, n.RootRecordId)));

        return rows;
    }

    private static bool IsAudited(PropertyEntry p)
    {
        if (p.Metadata.ClrType == typeof(byte[])) return false;
        return p.Metadata.PropertyInfo is not { } info || !info.IsDefined(typeof(NotAuditedAttribute), inherit: true);
    }

    private static string? PermissionOf(PropertyEntry p) =>
        p.Metadata.PropertyInfo?.GetCustomAttribute<FieldPermissionAttribute>()?.Permission;

    /// <summary>Everything a caller may see goes into one JSON snapshot; restricted values are returned separately.</summary>
    private static (string? Snapshot, List<(string Name, string? Value, string Permission)> Restricted) Split(
        IEnumerable<PropertyEntry> properties, Func<PropertyEntry, object?> read, bool nonNullOnly)
    {
        var plain = new Dictionary<string, string?>();
        var restricted = new List<(string, string?, string)>();

        foreach (var p in properties)
        {
            var value = Text(read(p));
            if (nonNullOnly && value is null) continue;

            if (PermissionOf(p) is { } permission) restricted.Add((p.Metadata.Name, value, permission));
            else plain[p.Metadata.Name] = value;
        }

        return (plain.Count == 0 ? null : JsonSerializer.Serialize(plain), restricted);
    }

    private static string RecordIdOf(EntityEntry entry)
    {
        var key = entry.Metadata.FindPrimaryKey();
        if (key is null) return string.Empty;
        return string.Join('|', key.Properties.Select(k => Text(entry.Property(k.Name).CurrentValue)));
    }

    private static Guid TenantOf(EntityEntry entry, Guid fallback) => entry.Entity switch
    {
        ITenantScopedEntity { TenantId: var t } when t != Guid.Empty => t,
        IGloballyExemptTenantScopedEntity { TenantId: { } t } when t != Guid.Empty => t,
        _ => fallback
    };

    private static string? Text(object? value) => value switch
    {
        null => null,
        DateTime d => d.ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset o => o.ToString("O", CultureInfo.InvariantCulture),
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        bool b => b ? "true" : "false",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString()
    };
}
