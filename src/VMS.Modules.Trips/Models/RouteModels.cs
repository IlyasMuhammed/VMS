namespace VMS.Modules.Trips.Models;

public sealed class RouteStopModel
{
    public long RouteStopId { get; set; }
    public int CityId { get; set; }
    public int Sequence { get; set; }
    public string StopType { get; set; } = string.Empty;
    public int? PlannedDurationMin { get; set; }
    public string? Remarks { get; set; }
}

public sealed class RouteModel
{
    public int RouteId { get; set; }
    public string RouteCode { get; set; } = string.Empty;
    public string RouteName { get; set; } = string.Empty;
    public int OriginCityId { get; set; }
    public int DestinationCityId { get; set; }
    public bool IsRoundTrip { get; set; }
    public decimal? DistanceKm { get; set; }
    public int? StandardDurationMin { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Remarks { get; set; }
    public IReadOnlyList<RouteStopModel> Stops { get; set; } = [];
}

public sealed class RouteStopInput
{
    public int CityId { get; set; }
    public string StopType { get; set; } = string.Empty;
    public int? PlannedDurationMin { get; set; }
    public string? Remarks { get; set; }
}

public sealed class CreateRouteRequest
{
    /// <summary>Auto-suggested (<c>RT-{OriginAbbr}-{DestAbbr}</c>, <c>-2</c> suffix if taken) when left blank.</summary>
    public string? RouteCode { get; set; }
    public string RouteName { get; set; } = string.Empty;
    public bool IsRoundTrip { get; set; }
    public decimal? DistanceKm { get; set; }
    public int? StandardDurationMin { get; set; }
    public string? Remarks { get; set; }
    public IReadOnlyList<RouteStopInput> Stops { get; set; } = [];
}

public sealed class UpdateRouteRequest
{
    public string RouteName { get; set; } = string.Empty;
    public bool IsRoundTrip { get; set; }
    public decimal? DistanceKm { get; set; }
    public int? StandardDurationMin { get; set; }
    public string? Status { get; set; }
    public string? Remarks { get; set; }
}

public sealed class UpdateRouteStopsRequest
{
    public IReadOnlyList<RouteStopInput> Stops { get; set; } = [];
}
