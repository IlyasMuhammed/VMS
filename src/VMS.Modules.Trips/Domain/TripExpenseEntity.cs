using VMS.Shared.Auditing;
using VMS.Shared.Common;

namespace VMS.Modules.Trips.Domain;

/// <summary>Trip expenses (§29): "A trip can have many expenses of configurable types." Fuel has its own screen
/// and table (<see cref="TripFuel"/>) and is never created here, even though its lookup value exists for
/// completeness in the shared <c>PlatformLookups.TripExpenseType</c> list.</summary>
internal sealed class TripExpense : ITenantScopedEntity, IAuditRooted
{
    public long TripExpenseId { get; set; }
    public Guid TenantId { get; set; }
    public long TripId { get; set; }

    public AuditRoot GetAuditRoot() => new("Trip", TripId.ToString());

    public DateTime ExpenseDate { get; set; }
    public int ExpenseTypeId { get; set; }
    public string? OtherExpenseType { get; set; }
    public string? Description { get; set; }
    public decimal? Quantity { get; set; }
    public decimal? Rate { get; set; }
    public decimal Amount { get; set; }
    public string? Reference { get; set; }
    public int? BusinessPartnerId { get; set; }
    public string PaymentMethod { get; set; } = TripExpensePaymentMethods.Cash;
    /// <summary>An already-uploaded <see cref="TripDocument"/> — reuses that upload path, the same choice this
    /// module has made for every other trip-level attachment (an issue's photo, a fuel receipt).</summary>
    public long? AttachmentId { get; set; }
    public string ApprovalStatus { get; set; } = TripExpenseApprovalStatuses.Pending;
    public string? RejectionReason { get; set; }
    public int? DecidedBy { get; set; }
    public DateTime? DecidedAtUtc { get; set; }
    public string Source { get; set; } = TripEventSources.Manual;

    public bool IsVoided { get; set; }
    public string? VoidReason { get; set; }
    public int? VoidedBy { get; set; }
    public DateTime? VoidedAtUtc { get; set; }
}

public static class TripExpensePaymentMethods
{
    public const string Cash = "Cash";
    public const string Card = "Card";
    public const string Bank = "Bank";
    public const string PaidByDriver = "PaidByDriver";
    public const string Other = "Other";
    public static readonly IReadOnlyList<string> All = [Cash, Card, Bank, PaidByDriver, Other];
}

/// <summary>§29: "Driver-app expenses start Pending (Recommended Design); approved by Ops/Fleet" — a back-office
/// (Manual-source) expense is entered by someone who already holds the approving role, so it starts Approved
/// directly rather than requiring the same person to immediately approve their own entry.</summary>
public static class TripExpenseApprovalStatuses
{
    public const string Pending = "Pending";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
}
