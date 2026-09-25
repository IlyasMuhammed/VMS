using VMS.Shared.Common;

namespace VMS.Modules.Trips.Domain;

/// <summary>Backs CC-25's own temporary <c>INV-YYYY-NNNNN</c> numbering, exactly the way <see cref="TripNumberCounter"/>
/// backs <c>TRP-YYYY-NNNNNN</c> — the same race-safe MERGE idiom, kept behind <c>IInvoiceNumberAllocator</c> so
/// CC-26 (still waiting on Q1: numbering format, per-customer prefix, fiscal-year reset) can swap the
/// implementation later without this task's own callers ever knowing the format changed.</summary>
internal sealed class InvoiceNumberCounter : ITenantScopedEntity
{
    public int InvoiceNumberCounterId { get; set; }
    public Guid TenantId { get; set; }
    public int Year { get; set; }
    public int LastNumber { get; set; }
}
