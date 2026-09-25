using VMS.Shared.Auditing;
using VMS.Shared.Common;

namespace VMS.Modules.Trips.Domain;

/// <summary>Fuel Card Management (§28). <see cref="VehicleId"/>/<see cref="DriverId"/> are a denormalized snapshot
/// of whichever <see cref="FuelCardAssignment"/> is currently open — "Current assignment (derived from
/// FuelCardAssignment)" — kept in sync by <c>FuelCardService.AssignAsync</c>, never set any other way.</summary>
internal sealed class FuelCard : ITenantScopedEntity, IAuditRooted
{
    public int FuelCardId { get; set; }
    public Guid TenantId { get; set; }

    public AuditRoot GetAuditRoot() => new("FuelCard", FuelCardId.ToString());

    /// <summary>§28: "Unique; displayed masked except last 4 digits." The raw number is kept only for the
    /// uniqueness check and never returned by any model — <c>FuelCardModel.MaskedCardNumber</c> is all a
    /// response ever carries.</summary>
    public string CardNumber { get; set; } = string.Empty;
    public int FuelCardCompanyId { get; set; }
    public int? VehicleId { get; set; }
    public int? DriverId { get; set; }
    public string? CardHolderName { get; set; }
    public DateOnly ExpiryDate { get; set; }
    public decimal? MonthlyLimit { get; set; }
    public string Status { get; set; } = FuelCardStatuses.Active;
    public string? Remarks { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public static class FuelCardStatuses
{
    public const string Active = "Active";
    public const string Inactive = "Inactive";
    public const string Expired = "Expired";
    public const string Blocked = "Blocked";
    public static readonly IReadOnlyList<string> All = [Active, Inactive, Expired, Blocked];

    /// <summary>What a caller may set directly through Save — Expired is system-only, set by the nightly job
    /// when ExpiryDate &lt; today (§28), never by hand.</summary>
    public static readonly IReadOnlyList<string> Settable = [Active, Inactive, Blocked];
}

/// <summary>"Supports assignment, reassignment (closes the current row, opens a new one), inactivation and
/// expiry [of the card itself]. No overlapping assignments per card" (§28) — satisfied structurally: the only way
/// to create a row is <c>FuelCardService.AssignAsync</c>, which always closes whatever was open first, so two
/// open rows for the same card can never coexist by construction. The filtered unique index below is the same
/// database-level backstop this codebase always adds alongside a service-level invariant, not the primary
/// mechanism (unlike <c>TripRateOverlap</c>'s own genuine arbitrary-range overlap check).</summary>
internal sealed class FuelCardAssignment : ITenantScopedEntity, IAuditRooted
{
    public long FuelCardAssignmentId { get; set; }
    public Guid TenantId { get; set; }
    public int FuelCardId { get; set; }

    public AuditRoot GetAuditRoot() => new("FuelCard", FuelCardId.ToString());

    public int? VehicleId { get; set; }
    public int? DriverId { get; set; }
    public DateOnly AssignedFrom { get; set; }
    public DateOnly? AssignedTo { get; set; }
    public string? Reason { get; set; }
}
