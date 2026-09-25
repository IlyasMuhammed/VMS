namespace VMS.Modules.Trips.Models;

public sealed class BankCashAccountModel
{
    public long BankCashAccountId { get; set; }
    public string AccountTitle { get; set; } = string.Empty;
    public string BankName { get; set; } = string.Empty;
    public string BranchName { get; set; } = string.Empty;
    public string AccountNumberLast4 { get; set; } = string.Empty;
    public string CurrencyCode { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class SaveBankCashAccountRequest
{
    public string AccountTitle { get; set; } = string.Empty;
    public string BankName { get; set; } = string.Empty;
    public string BranchName { get; set; } = string.Empty;
    public string AccountNumberLast4 { get; set; } = string.Empty;
    public string? CurrencyCode { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>Required to update an existing account; omitted when creating a new one.</summary>
    public string? RowVersion { get; set; }
}
