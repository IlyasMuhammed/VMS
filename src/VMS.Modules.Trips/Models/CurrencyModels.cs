namespace VMS.Modules.Trips.Models;

public sealed class CurrencyModel
{
    public int CurrencyId { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public string CurrencyName { get; set; } = string.Empty;
    public string? Symbol { get; set; }
    public byte DecimalPlaces { get; set; }
    public bool IsBase { get; set; }
    public string Status { get; set; } = string.Empty;
}

public sealed class SaveCurrencyRequest
{
    /// <summary>Only read on create; a currency's code is immutable once it exists (§13A's own field table has no
    /// "code is editable" rule, and every downstream table stamps this code onto its own rows).</summary>
    public string CurrencyCode { get; set; } = string.Empty;
    public string CurrencyName { get; set; } = string.Empty;
    public string? Symbol { get; set; }
    public byte DecimalPlaces { get; set; } = 2;
    public string Status { get; set; } = Domain.CurrencyStatuses.Active;
}

public sealed class CurrencySettingsModel
{
    public string BaseCurrencyCode { get; set; } = string.Empty;
    public bool MultiCurrencyEnabled { get; set; }
}

public sealed class UpdateCurrencySettingsRequest
{
    public string? BaseCurrencyCode { get; set; }
    public bool MultiCurrencyEnabled { get; set; }
}

public sealed class ExchangeRateModel
{
    public int ExchangeRateId { get; set; }
    public string FromCurrencyCode { get; set; } = string.Empty;
    public string ToCurrencyCode { get; set; } = string.Empty;
    public DateOnly RateDate { get; set; }
    public decimal Rate { get; set; }
}

public sealed class SaveExchangeRateRequest
{
    public string FromCurrencyCode { get; set; } = string.Empty;
    public DateOnly? RateDate { get; set; }
    public decimal? Rate { get; set; }
}
