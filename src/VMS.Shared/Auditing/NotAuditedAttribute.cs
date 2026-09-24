namespace VMS.Shared.Auditing;

/// <summary>
/// Keeps an entity, or a single property, out of the audit trail. Use it for secrets (password and
/// token hashes), for bookkeeping that changes on every request (last sign-in, failed-attempt counters)
/// and for tables that are themselves logs. Binary properties are never audited.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property)]
public sealed class NotAuditedAttribute : Attribute;
