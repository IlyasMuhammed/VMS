using VMS.Shared.Common;

namespace VMS.Modules.Trips.Domain;

/// <summary>Backs <c>LED-YYYY-NNNNNN</c> numbering (§40A.2) — the same race-safe MERGE idiom as
/// <see cref="TripNumberCounter"/>/<see cref="InvoiceNumberCounter"/>, one tenant-wide sequence per year.</summary>
internal sealed class LedgerNumberCounter : ITenantScopedEntity
{
    public int LedgerNumberCounterId { get; set; }
    public Guid TenantId { get; set; }
    public int Year { get; set; }
    public int LastNumber { get; set; }
}

/// <summary>Backs <see cref="CustomerLedgerEntry.CustomerSeq"/> — a separate, per-customer (not per-year)
/// monotonic counter, "for stable running balance order" (§40A.2).</summary>
internal sealed class CustomerLedgerSequenceCounter : ITenantScopedEntity
{
    public long CustomerLedgerSequenceCounterId { get; set; }
    public Guid TenantId { get; set; }
    public int CustomerId { get; set; }
    public long LastSeq { get; set; }
}
