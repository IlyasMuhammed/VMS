using VMS.Shared.Auditing;
using VMS.Shared.Common;

namespace VMS.Modules.Trips.Domain;

/// <summary>
/// The city master used by routes, trip configurations, open trips and addresses (FSD §16) — a dedicated,
/// richer table for this module's own routing needs, deliberately separate from the *existing*
/// <c>core.LookupValues</c> <c>CITY</c> type that Customer's own address fields (§10/§12) and Business Partner's
/// (first FSD) already use for a plain address city. This one needs a globally-unique <see cref="Abbreviation"/>
/// (route codes like <c>RT-LHR-FSD</c> embed it) that the generic lookup type has no room for.
/// </summary>
internal sealed class City : ITenantScopedEntity, IAuditRooted
{
    public int CityId { get; set; }
    public Guid TenantId { get; set; }

    public AuditRoot GetAuditRoot() => new("City", CityId.ToString());

    public string CityName { get; set; } = string.Empty;
    /// <summary>2–5 upper-case letters, unique across the whole tenant (not just within a country/province) — a
    /// route code embeds it directly.</summary>
    public string Abbreviation { get; set; } = string.Empty;
    public int CountryId { get; set; }
    public string? ProvinceState { get; set; }
    public string Status { get; set; } = ActiveInactiveStatuses.Active;
}
