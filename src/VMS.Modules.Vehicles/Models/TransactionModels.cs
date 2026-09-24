using VMS.Shared.Authorization;

namespace VMS.Modules.Vehicles.Models;

/// <summary>One entry of a vehicle's ledger (FSD §19). Entries are never edited or deleted: a mistake is corrected by a reversing entry (BR-VH-011).</summary>
public class TransactionModel
{
    public int Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string? SubType { get; set; }
    /// <summary>Negative for an adjustment that reverses a payment or cost.</summary>
    [FieldPermission(PermissionCodes.VEH_FIELD_COST_VIEW)] public decimal Amount { get; set; }
    public DateOnly Date { get; set; }
    public PartnerRef? Partner { get; set; }
    public string? Reference { get; set; }
    public string Source { get; set; } = string.Empty;
    public bool IsSystemGenerated { get; set; }
    public string? Reason { get; set; }
    /// <summary>For an adjustment: the entry it reverses.</summary>
    public int? ReversesTransactionId { get; set; }
    /// <summary>True once a reversing entry has been posted against this one.</summary>
    public bool IsReversed { get; set; }
    /// <summary>A receipt was uploaded for this entry (source §7).</summary>
    public bool HasReceipt { get; set; }
    public string? ReceiptFileName { get; set; }
    public DateTime CreatedOn { get; set; }
}

/// <summary>A stored receipt, ready to stream back to whoever asked for it.</summary>
public sealed record ReceiptFile(Stream Content, string ContentType, string FileName);

/// <summary>The controlled adjustment route (BR-VH-011): a reversing entry with a reason.</summary>
public class ReverseTransactionRequest
{
    /// <summary>Why the entry is being reversed. Required.</summary>
    public string? Reason { get; set; }
    /// <summary>The day of the reversal. Defaults to today; not in the future and not before the entry it reverses.</summary>
    public DateOnly? Date { get; set; }
}
