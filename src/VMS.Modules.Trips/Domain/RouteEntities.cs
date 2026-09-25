using VMS.Shared.Auditing;
using VMS.Shared.Common;

namespace VMS.Modules.Trips.Domain;

/// <summary>A general, customer-independent corridor with ordered stops (FSD §17), e.g. <c>RT-LHR-FSD</c>. Origin
/// and destination are derived from the stop list, not entered directly.</summary>
internal sealed class Route : ITenantScopedEntity, IAuditRooted
{
    public int RouteId { get; set; }
    public Guid TenantId { get; set; }

    public AuditRoot GetAuditRoot() => new("Route", RouteId.ToString());

    public string RouteCode { get; set; } = string.Empty;
    public string RouteName { get; set; } = string.Empty;
    public int OriginCityId { get; set; }
    public int DestinationCityId { get; set; }
    public bool IsRoundTrip { get; set; }
    public decimal? DistanceKm { get; set; }
    public int? StandardDurationMin { get; set; }
    public string Status { get; set; } = ActiveInactiveStatuses.Active;
    public string? Remarks { get; set; }

    /// <summary>Set once a trip configuration (CC-10) references this route — from then on, its stops are
    /// reordered by creating a new route instead (§17's own "Recommended Design"), not by editing this one.
    /// Always false until CC-10 exists; a documented no-op the same way City's abbreviation-lock is.</summary>
    public bool IsLocked { get; set; }
}

internal sealed class RouteStop : ITenantScopedEntity, IAuditRooted
{
    public long RouteStopId { get; set; }
    public Guid TenantId { get; set; }
    public int RouteId { get; set; }

    public AuditRoot GetAuditRoot() => new("Route", RouteId.ToString());

    public int CityId { get; set; }
    /// <summary>1..n, unique per route, no gaps — 1 is always Origin, the highest is always Destination.</summary>
    public int Sequence { get; set; }
    public string StopType { get; set; } = RouteStopTypes.Via;
    public int? PlannedDurationMin { get; set; }
    public string? Remarks { get; set; }
}

public static class RouteStopTypes
{
    public const string Origin = "Origin";
    public const string Pickup = "Pickup";
    public const string Via = "Via";
    public const string Delivery = "Delivery";
    public const string Destination = "Destination";
    public static readonly IReadOnlyList<string> All = [Origin, Pickup, Via, Delivery, Destination];
}
