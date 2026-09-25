using VMS.Shared.Auditing;
using VMS.Shared.Common;

namespace VMS.Modules.Trips.Domain;

/// <summary>A fixed-trip configuration, belonging to exactly one customer (FSD §18) — never shared across
/// customers, even for the same physical <see cref="Route"/>.</summary>
internal sealed class TripConfiguration : ITenantScopedEntity, IAuditRooted
{
    public long TripConfigurationId { get; set; }
    public Guid TenantId { get; set; }
    public int CustomerId { get; set; }

    public AuditRoot GetAuditRoot() => new("TripConfiguration", TripConfigurationId.ToString());

    /// <summary>Unique per customer (not tenant-wide) — two customers may both use e.g. <c>...-LHR-FSD-01</c>.</summary>
    public string TripCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int RouteId { get; set; }
    public string DirectionType { get; set; } = TripDirectionTypes.OneWay;
    public string Status { get; set; } = TripConfigurationStatuses.Draft;
    public string? Remarks { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public static class TripDirectionTypes
{
    public const string OneWay = "OneWay";
    public const string Return = "Return";
    public const string RoundTrip = "RoundTrip";
    public static readonly IReadOnlyList<string> All = [OneWay, Return, RoundTrip];
}

public static class TripConfigurationStatuses
{
    public const string Draft = "Draft";
    public const string Active = "Active";
    public const string Inactive = "Inactive";
    public static readonly IReadOnlyList<string> All = [Draft, Active, Inactive];
}

/// <summary>Copied from the <see cref="Route"/> on creation, then freely adjustable for this one customer (an
/// extra customer site, for example) — FSD §18: "changes after trips exist affect only new trips; trips copy stops
/// to TripStop" (Trip doesn't exist yet — a later CC task).</summary>
internal sealed class TripConfigurationStop : ITenantScopedEntity, IAuditRooted
{
    public long TripConfigurationStopId { get; set; }
    public Guid TenantId { get; set; }
    public long TripConfigurationId { get; set; }

    public AuditRoot GetAuditRoot() => new("TripConfiguration", TripConfigurationId.ToString());

    /// <summary>Exactly one of <see cref="CityId"/>/<see cref="OtherLocation"/> is set — a customer site that is
    /// not itself in the city master.</summary>
    public int? CityId { get; set; }
    public string? OtherLocation { get; set; }
    public int Sequence { get; set; }
    public string StopType { get; set; } = RouteStopTypes.Via;
}

/// <summary>A vehicle allowed on a trip configuration (FSD §19) — never a single VehicleId on the configuration
/// itself, since a configuration has many, effective-dated, and the same vehicle can be allowed on several
/// configurations across several customers.</summary>
internal sealed class TripConfigurationVehicle : ITenantScopedEntity, IAuditRooted
{
    public long TripConfigurationVehicleId { get; set; }
    public Guid TenantId { get; set; }
    public long TripConfigurationId { get; set; }

    public AuditRoot GetAuditRoot() => new("TripConfiguration", TripConfigurationId.ToString());

    /// <summary>The existing Vehicle module's own id — no FK (cross-module; resolved through <c>IVehicleDirectory</c>).</summary>
    public int VehicleId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public string Status { get; set; } = ActiveInactiveStatuses.Active;
    public string? Remarks { get; set; }
}
