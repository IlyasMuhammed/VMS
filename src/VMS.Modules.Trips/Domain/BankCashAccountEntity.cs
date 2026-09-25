using VMS.Shared.Auditing;
using VMS.Shared.Common;

namespace VMS.Modules.Trips.Domain;

/// <summary>§37: the company's own bank accounts that receive customer payments. Built now (CC-31); real seed
/// data waits on Q3 (bank, branch, title, last 4 digits) — until then, Admin enters accounts by hand through this
/// same CRUD.</summary>
internal sealed class BankCashAccount : ITenantScopedEntity, IAuditRooted
{
    public long BankCashAccountId { get; set; }
    public Guid TenantId { get; set; }

    public AuditRoot GetAuditRoot() => new("BankCashAccount", BankCashAccountId.ToString());

    public string AccountTitle { get; set; } = string.Empty;
    public string BankName { get; set; } = string.Empty;
    public string BranchName { get; set; } = string.Empty;
    /// <summary>Only the last 4 digits are ever stored — the same masking discipline CC-18's own `FuelCard`
    /// already established for card numbers.</summary>
    public string AccountNumberLast4 { get; set; } = string.Empty;
    public string CurrencyCode { get; set; } = "PKR";
    public bool IsActive { get; set; } = true;
    public byte[] RowVersion { get; set; } = [];
}
