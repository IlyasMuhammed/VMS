namespace VMS.Modules.Vehicles.Models;

/// <summary>Filters for the Payables Due workbench (FSD §19A.5).</summary>
public class PayablesQuery
{
    public int? ChargeTypeId { get; set; }
    public int? PayeeId { get; set; }
    public Guid? BranchId { get; set; }
    public DateOnly? DueFrom { get; set; }
    public DateOnly? DueTo { get; set; }
    /// <summary>Overdue entries only, for the tile's drill-down.</summary>
    public bool OverdueOnly { get; set; }
}
