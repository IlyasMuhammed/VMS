using VMS.Shared.Auditing;
using VMS.Shared.Common;

namespace VMS.Modules.Core.Domain;

/// <summary>
/// A tenant's numbering rule for one kind of record: the prefix, the digit count and when the count
/// starts again. Changing it affects numbers issued from then on; numbers already issued keep their form.
/// </summary>
internal class NumberSeries : ITenantScopedEntity
{
    public int NumberSeriesID { get; set; }
    public Guid TenantId { get; set; }
    /// <summary>Which record the series numbers, for example <c>BP</c>. See <c>NumberSeriesCodes</c>.</summary>
    public string Code { get; set; } = string.Empty;
    public string Prefix { get; set; } = string.Empty;
    public int Padding { get; set; }
    public string ResetPeriod { get; set; } = string.Empty;
}

/// <summary>The last number issued in one period of a series. Bookkeeping; the audit trail is on the series, not on every increment.</summary>
[NotAudited]
internal class NumberSeriesCounter
{
    public int NumberSeriesID { get; set; }
    /// <summary>The year (<c>2026</c>), the month (<c>202603</c>) or empty for a series that never resets.</summary>
    public string PeriodKey { get; set; } = string.Empty;
    public long LastNumber { get; set; }
}
