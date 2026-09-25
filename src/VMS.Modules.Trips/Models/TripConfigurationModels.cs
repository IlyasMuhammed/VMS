namespace VMS.Modules.Trips.Models;

public sealed class TripConfigurationStopModel
{
    public long TripConfigurationStopId { get; set; }
    public int? CityId { get; set; }
    public string? OtherLocation { get; set; }
    public int Sequence { get; set; }
    public string StopType { get; set; } = string.Empty;
}

public sealed class TripConfigurationStopInput
{
    public int? CityId { get; set; }
    public string? OtherLocation { get; set; }
    public string StopType { get; set; } = string.Empty;
}

public sealed class TripConfigurationModel
{
    public long TripConfigurationId { get; set; }
    public int CustomerId { get; set; }
    public string TripCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int RouteId { get; set; }
    public string DirectionType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Remarks { get; set; }
    public string RowVersion { get; set; } = string.Empty;
    public IReadOnlyList<TripConfigurationStopModel> Stops { get; set; } = [];
    /// <summary>Non-blocking warnings from the last action (currently just Activate's "≥1 rate" check, §18) —
    /// empty outside of that response.</summary>
    public IReadOnlyList<string> Warnings { get; set; } = [];
}

public sealed class CreateTripConfigurationRequest
{
    public int CustomerId { get; set; }
    /// <summary>Auto-suggested (<c>{CustShort}-{Orig}-{Dest}-01</c>, or <c>-RT</c> for a round trip) when left blank.</summary>
    public string? TripCode { get; set; }
    public string Name { get; set; } = string.Empty;
    public int RouteId { get; set; }
    public string DirectionType { get; set; } = string.Empty;
    public string? Remarks { get; set; }
}

public sealed class UpdateTripConfigurationRequest
{
    public string Name { get; set; } = string.Empty;
    public string DirectionType { get; set; } = string.Empty;
    public string? Remarks { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class ChangeTripConfigurationStatusRequest
{
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class UpdateTripConfigurationStopsRequest
{
    public IReadOnlyList<TripConfigurationStopInput> Stops { get; set; } = [];
}

public sealed class CopyTripConfigurationRequest
{
    public int? CustomerId { get; set; }
    public string? TripCode { get; set; }
    public string? Name { get; set; }
}

public sealed class TripConfigurationVehicleModel
{
    public long TripConfigurationVehicleId { get; set; }
    public int VehicleId { get; set; }
    public string? VehicleCode { get; set; }
    public string? RegistrationNo { get; set; }
    public string? Category { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Remarks { get; set; }
}

public sealed class AssignTripConfigurationVehicleRequest
{
    public int VehicleId { get; set; }
    public DateOnly? EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public string? Remarks { get; set; }
}

public sealed class UpdateTripConfigurationVehicleRequest
{
    public DateOnly? EffectiveTo { get; set; }
    public string? Status { get; set; }
    public string? Remarks { get; set; }
}
