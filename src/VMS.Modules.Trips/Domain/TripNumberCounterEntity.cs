using VMS.Shared.Common;

namespace VMS.Modules.Trips.Domain;

/// <summary>
/// Backs this module's own trip numbering (§21/§23: <c>TRP-YYYY-NNNNNN</c>, a 4-digit year). Deliberately not the
/// shared <c>VMS.Shared.Numbering.INumberSeries</c>/<c>core.NumberSeries</c> mechanism every other module's own
/// number uses: that mechanism's <c>NumberFormat.Format</c> hard-codes a 2-digit year (<c>BP-26-00147</c>) to match
/// the *first* FSD's own convention, and changing it to be configurable would mean a migration on the already-
/// shipped Core module for one format quirk this FSD alone asks for. A small, self-contained counter in this
/// module's own schema, using the identical race-safe MERGE idiom, is the lower-blast-radius choice — see
/// <c>Document/VMS-TripBilling-Implementation-Notes.md</c> for this kind of deviation's own general rule.
/// </summary>
internal sealed class TripNumberCounter : ITenantScopedEntity
{
    public int TripNumberCounterId { get; set; }
    public Guid TenantId { get; set; }
    public int Year { get; set; }
    public int LastNumber { get; set; }
}
