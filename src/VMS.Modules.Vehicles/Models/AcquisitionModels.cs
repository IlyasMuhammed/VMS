using VMS.Shared.Authorization;

namespace VMS.Modules.Vehicles.Models;

/// <summary>The acquisition block of a Draft vehicle (FSD §18.1). Nothing here is posted until the vehicle is activated (§19).</summary>
public class SaveAcquisitionRequest
{
    /// <summary>The day the vehicle entered the fleet. Not in the future.</summary>
    public DateOnly? AcquisitionDate { get; set; }
    /// <summary>Purchase, Lease, Rent, SharedInduction or CustomerInduction.</summary>
    public string? AcquisitionType { get; set; }
    /// <summary>Any active partner: a vendor, a dealer, a person.</summary>
    public int? SellerId { get; set; }
    public decimal? PurchasePrice { get; set; }
    /// <summary>From 0 to the purchase price. Required once a purchase price is entered.</summary>
    public decimal? AmountPaid { get; set; }
    /// <summary>Cash, BankTransfer, Cheque or PayOrder. Required when something was paid.</summary>
    public string? PaymentMode { get; set; }
    public string? PaymentReference { get; set; }
    public decimal? RegistrationCost { get; set; }
    /// <summary>The vehicle's row version, so a change made by someone else in the meantime is not overwritten.</summary>
    public string RowVersion { get; set; } = string.Empty;
}

public class AcquisitionModel
{
    public int VehicleId { get; set; }
    public string VehicleStatus { get; set; } = string.Empty;
    public DateOnly? AcquisitionDate { get; set; }
    public string? AcquisitionType { get; set; }
    public PartnerRef? Seller { get; set; }
    public int? SellerId { get; set; }
    [FieldPermission(PermissionCodes.VEH_FIELD_COST_VIEW)] public decimal? PurchasePrice { get; set; }
    [FieldPermission(PermissionCodes.VEH_FIELD_COST_VIEW)] public decimal? AmountPaid { get; set; }
    public string? PaymentMode { get; set; }
    public string? PaymentReference { get; set; }
    [FieldPermission(PermissionCodes.VEH_FIELD_COST_VIEW)] public decimal? RegistrationCost { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}
