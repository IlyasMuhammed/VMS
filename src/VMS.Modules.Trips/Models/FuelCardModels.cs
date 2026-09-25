namespace VMS.Modules.Trips.Models;

public sealed class FuelCardModel
{
    public int FuelCardId { get; set; }
    /// <summary>§28: masked except the last 4 digits, e.g. <c>****1234</c>. The full number never leaves the server.</summary>
    public string MaskedCardNumber { get; set; } = string.Empty;
    public int FuelCardCompanyId { get; set; }
    public int? VehicleId { get; set; }
    public int? DriverId { get; set; }
    public string? CardHolderName { get; set; }
    public DateOnly ExpiryDate { get; set; }
    public decimal? MonthlyLimit { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Remarks { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class CreateFuelCardRequest
{
    public string CardNumber { get; set; } = string.Empty;
    public int FuelCardCompanyId { get; set; }
    public string? CardHolderName { get; set; }
    public DateOnly ExpiryDate { get; set; }
    public decimal? MonthlyLimit { get; set; }
    public string? Remarks { get; set; }
}

/// <summary>The card number and its issuing company are set once, at creation, and not editable here — the same
/// "identifier, not a field" treatment this codebase gives a City's Abbreviation or a Currency's code.</summary>
public sealed class SaveFuelCardRequest
{
    public string? CardHolderName { get; set; }
    public DateOnly ExpiryDate { get; set; }
    public decimal? MonthlyLimit { get; set; }
    /// <summary>Restricted server-side to <c>FuelCardStatuses.Settable</c> — Expired cannot be set by hand.</summary>
    public string Status { get; set; } = string.Empty;
    public string? Remarks { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class AssignFuelCardRequest
{
    public int? VehicleId { get; set; }
    public int? DriverId { get; set; }
    /// <summary>Defaults to today.</summary>
    public DateOnly? AssignedFrom { get; set; }
    public string? Reason { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class FuelCardAssignmentModel
{
    public long FuelCardAssignmentId { get; set; }
    public int FuelCardId { get; set; }
    public int? VehicleId { get; set; }
    public int? DriverId { get; set; }
    public DateOnly AssignedFrom { get; set; }
    public DateOnly? AssignedTo { get; set; }
    public string? Reason { get; set; }
}
