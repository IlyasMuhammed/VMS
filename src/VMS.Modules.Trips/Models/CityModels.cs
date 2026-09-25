namespace VMS.Modules.Trips.Models;

public sealed class CityModel
{
    public int CityId { get; set; }
    public string CityName { get; set; } = string.Empty;
    public string Abbreviation { get; set; } = string.Empty;
    public int CountryId { get; set; }
    public string? ProvinceState { get; set; }
    public string Status { get; set; } = string.Empty;
    /// <summary>What the UI always shows (§16): <c>CityName (ABBR)</c>.</summary>
    public string Display => $"{CityName} ({Abbreviation})";
}

public sealed class SaveCityRequest
{
    public string CityName { get; set; } = string.Empty;
    public string Abbreviation { get; set; } = string.Empty;
    public int? CountryId { get; set; }
    public string? ProvinceState { get; set; }
    public string Status { get; set; } = Domain.ActiveInactiveStatuses.Active;
}
