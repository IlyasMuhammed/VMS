namespace VMS.Modules.Core.Models;

public class NumberSeriesModel
{
    public string Code { get; set; } = string.Empty;
    public string Entity { get; set; } = string.Empty;
    public string Prefix { get; set; } = string.Empty;
    public int Padding { get; set; }
    /// <summary>Yearly, Monthly or Never.</summary>
    public string ResetPeriod { get; set; } = string.Empty;
    /// <summary>The number the next record will get if it is saved today. Shown as a preview; nothing is reserved.</summary>
    public string NextNumber { get; set; } = string.Empty;
}

public class UpdateNumberSeriesRequest
{
    public string Prefix { get; set; } = string.Empty;
    public int Padding { get; set; }
    public string ResetPeriod { get; set; } = string.Empty;
}
