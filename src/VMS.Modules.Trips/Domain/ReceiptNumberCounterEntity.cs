using VMS.Shared.Common;

namespace VMS.Modules.Trips.Domain;

/// <summary>Backs <c>RCPT-YYYY-NNNNN</c> numbering (§37) — the same race-safe MERGE idiom as
/// <see cref="InvoiceNumberCounter"/>, one tenant-wide sequence per year.</summary>
internal sealed class ReceiptNumberCounter : ITenantScopedEntity
{
    public int ReceiptNumberCounterId { get; set; }
    public Guid TenantId { get; set; }
    public int Year { get; set; }
    public int LastNumber { get; set; }
}
