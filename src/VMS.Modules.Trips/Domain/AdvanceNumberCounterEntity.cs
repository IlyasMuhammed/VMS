using VMS.Shared.Common;

namespace VMS.Modules.Trips.Domain;

/// <summary>Backs <c>ADV-YYYY-NNNNN</c> numbering (§46.6) — the same race-safe MERGE idiom as
/// <see cref="ReceiptNumberCounter"/>.</summary>
internal sealed class AdvanceNumberCounter : ITenantScopedEntity
{
    public int AdvanceNumberCounterId { get; set; }
    public Guid TenantId { get; set; }
    public int Year { get; set; }
    public int LastNumber { get; set; }
}
