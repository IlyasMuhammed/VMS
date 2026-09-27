using VMS.Shared.Common;

namespace VMS.Modules.Trips.Domain;

/// <summary>Backs CC-26's own <c>INV-YYYY-MM-NNNN</c> numbering (Q1's answer: "INV-2026-01-0001" — a 4-digit
/// sequence, resetting every calendar month, no per-customer prefix) — the same race-safe MERGE idiom as
/// <see cref="TripNumberCounter"/>, just keyed one level finer (tenant, year AND month) than that one's own
/// yearly reset. Kept behind <c>IInvoiceNumberAllocator</c>, the seam CC-25 built specifically so this task never
/// had to touch <see cref="Services.InvoiceCreationService"/> itself.</summary>
internal sealed class InvoiceNumberCounter : ITenantScopedEntity
{
    public int InvoiceNumberCounterId { get; set; }
    public Guid TenantId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public int LastNumber { get; set; }
}
