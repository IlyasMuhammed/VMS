using VMS.Shared.Auditing;
using VMS.Shared.Common;

namespace VMS.Modules.Trips.Domain;

/// <summary>A rate specific to Customer + Trip Configuration + effective date range (FSD §26) — can last a single
/// day, must never overlap another Active rate of the same customer/configuration. A trip snapshots whichever
/// rate resolves for its own date and keeps that snapshot even if this row is edited later.</summary>
internal sealed class TripRate : ITenantScopedEntity, IAuditRooted
{
    public long TripRateId { get; set; }
    public Guid TenantId { get; set; }
    /// <summary>Denormalized, always equal to the configuration's own <c>CustomerId</c> — kept on the row because
    /// every rate query is naturally "for this customer," the same reasoning <see cref="TripConfiguration"/> itself
    /// documents nowhere else needs restating.</summary>
    public int CustomerId { get; set; }
    public long TripConfigurationId { get; set; }

    public AuditRoot GetAuditRoot() => new("TripConfiguration", TripConfigurationId.ToString());

    public DateOnly EffectiveFrom { get; set; }
    /// <summary>Null = open-ended: in force until a later rate starts (§26, "Confirmed").</summary>
    public DateOnly? EffectiveTo { get; set; }
    public decimal RateAmount { get; set; }
    public string CurrencyCode { get; set; } = "PKR";
    public string Status { get; set; } = ActiveInactiveStatuses.Active;
    public string? Remarks { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
