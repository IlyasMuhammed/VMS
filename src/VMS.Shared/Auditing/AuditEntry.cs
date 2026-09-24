using VMS.Shared.Common;

namespace VMS.Shared.Auditing;

/// <summary>
/// One line of the audit trail (FSD §13.2): what changed, on which record, from what to what, who did
/// it and when. Rows are only ever inserted. The table is owned by the Core module; every other module
/// writes to it through its own DbContext so the audit row commits with the change it describes.
/// </summary>
public sealed class AuditEntry : ITenantScopedEntity
{
    public long AuditEntryID { get; set; }

    /// <summary>Server clock, UTC. A user's own clock is not trusted for an audit timestamp.</summary>
    public DateTime OccurredAt { get; set; }

    public Guid TenantId { get; set; }

    /// <summary>Every entry written during one request shares a group, so a screen can replay one operation as a unit.</summary>
    public Guid GroupId { get; set; }

    /// <summary>The kind of record, for example <c>UserAccount</c>.</summary>
    public string Entity { get; set; } = string.Empty;

    /// <summary>The record's key. A composite key is joined with <c>|</c>.</summary>
    public string RecordId { get; set; } = string.Empty;

    /// <summary>
    /// The record this change belongs to, when the changed record is part of a larger one (a partner's contact belongs to
    /// the partner). A history screen reads everything for one root with a single query. Null for a record that stands alone.
    /// </summary>
    public string? RootEntity { get; set; }

    public string? RootRecordId { get; set; }

    /// <summary>See <see cref="AuditActions"/>; explicit notes may use their own verbs (for example <c>DuplicateOverridden</c>).</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>The changed field for an update. Empty for a whole-record entry.</summary>
    public string? Field { get; set; }

    /// <summary>The value before the change, or for a deleted record a JSON snapshot of it.</summary>
    public string? OldValue { get; set; }

    /// <summary>The value after the change, or for a created record a JSON snapshot of it.</summary>
    public string? NewValue { get; set; }

    public string? Reason { get; set; }

    /// <summary>
    /// The field permission needed to see the values, when the field is restricted (for example the
    /// purchase price). A history screen or export must hide <see cref="OldValue"/> and
    /// <see cref="NewValue"/> from anyone who lacks it (BR-SEC-003).
    /// </summary>
    public string? RequiredPermission { get; set; }

    /// <summary>Null for work done by the system with nobody signed in.</summary>
    public int? UserId { get; set; }

    public string? UserName { get; set; }

    public string? IpAddress { get; set; }
}

public static class AuditActions
{
    public const string Created = "Created";
    public const string Updated = "Updated";
    public const string Deleted = "Deleted";
}
