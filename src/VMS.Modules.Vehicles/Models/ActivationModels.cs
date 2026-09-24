using VMS.Shared.Authorization;

namespace VMS.Modules.Vehicles.Models;

/// <summary>A due date the person corrected on the schedule preview, for a bank whose schedule is not regular (FR-VH-005).</summary>
public class ScheduleDueDate
{
    public int InstallmentNo { get; set; }
    public DateOnly DueDate { get; set; }
}

/// <summary>
/// What is sent to check or perform an activation (FSD §21 step 5). The ownership category and its details are sent here because a
/// Draft holds no relation: it is opened, dated by the acquisition date, together with everything else (FR-VH-012).
/// </summary>
public class ActivateVehicleRequest
{
    /// <summary>SelfOwned, Shared, Rented, CustomerArrangement or BankLeased.</summary>
    public string Category { get; set; } = string.Empty;
    public CategoryDetails Details { get; set; } = new();
    /// <summary>The items entered on the Draft (step 4). They are attached, and their costs posted as major expenses, when the vehicle is activated.</summary>
    public List<AttachItemRequest> Items { get; set; } = [];
    /// <summary>Installments whose due dates differ from the generated ones.</summary>
    public List<ScheduleDueDate> DueDates { get; set; } = [];
    /// <summary>The fuel card is on another vehicle in the fleet: say true to take it from that vehicle (VAL-VH-018).</summary>
    public bool ReassignFuelCard { get; set; }
    /// <summary>The default driver has another vehicle: say true to release that assignment (VAL-VH-017).</summary>
    public bool ReleaseFromOther { get; set; }
    /// <summary>The vehicle's row version. Required to activate, not to check.</summary>
    public string? RowVersion { get; set; }
}

public class ChecklistItem
{
    public string Code { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool Ok { get; set; }
    /// <summary>False for something worth knowing that does not stop the activation (a missing registration book on a rented vehicle).</summary>
    public bool Blocking { get; set; } = true;
    /// <summary>What is missing or wrong, in words.</summary>
    public string? Message { get; set; }
}

public class PostingModel
{
    public string Type { get; set; } = string.Empty;
    public string? SubType { get; set; }
    [FieldPermission(PermissionCodes.VEH_FIELD_COST_VIEW)] public decimal Amount { get; set; }
    public DateOnly Date { get; set; }
    public PartnerRef? Partner { get; set; }
    public string Reference { get; set; } = string.Empty;
}

public class ScheduleRowModel
{
    public int InstallmentNo { get; set; }
    public DateOnly DueDate { get; set; }
    [FieldPermission(PermissionCodes.VEH_FIELD_FINANCE_VIEW)] public decimal Amount { get; set; }
    public bool IsResidual { get; set; }
}

/// <summary>What is missing before a Draft can be activated, and what activating would write.</summary>
public class ActivationCheck
{
    public bool CanActivate { get; set; }
    public List<ChecklistItem> Items { get; set; } = [];
    /// <summary>The ledger entries the activation would write (FR-VH-011). Empty until an acquisition date and price are entered.</summary>
    public List<PostingModel> Postings { get; set; } = [];
    /// <summary>The schedule the activation would write, with any corrected due dates applied.</summary>
    public List<ScheduleRowModel> Schedule { get; set; } = [];
}
