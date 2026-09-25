namespace VMS.Modules.Trips.Models;

public sealed class LocationInput
{
    public string LocationType { get; set; } = string.Empty;
    public int? CityId { get; set; }
    public string? OtherLocationType { get; set; }
    public string? OtherLocationName { get; set; }
    public int? OtherNearestCityId { get; set; }
}

public sealed class LocationModel
{
    public string LocationType { get; set; } = string.Empty;
    public int? CityId { get; set; }
    public string? OtherLocationType { get; set; }
    public string? OtherLocationName { get; set; }
    public int? OtherNearestCityId { get; set; }
    /// <summary>What the FSD's own example shows on an invoice route label — the city abbreviation, or the typed
    /// site name for an Other Location.</summary>
    public string Label { get; set; } = string.Empty;
}

public sealed class CreateOpenTripRequest
{
    public int CustomerId { get; set; }
    public LocationInput From { get; set; } = new();
    public LocationInput To { get; set; } = new();
    public IReadOnlyList<LocationInput> Stops { get; set; } = [];
    public bool IsRoundTrip { get; set; }
    public int VehicleId { get; set; }
    public int? DriverId { get; set; }
    public string? DriverOverrideReason { get; set; }
    public decimal TripAmount { get; set; }
    public DateOnly TripDate { get; set; }
    public string? CustomerTripReference { get; set; }
    public DateTime? PlannedStart { get; set; }
    public string? Remarks { get; set; }
}
