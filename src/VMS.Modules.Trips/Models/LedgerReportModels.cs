using VMS.Shared.Pagination;

namespace VMS.Modules.Trips.Models;

/// <summary>§42's own report shape, shared across all fifteen LED-xx reports: wildly different column sets per
/// report rule out one fixed DTO per report without a lot of near-duplicate classes, so each row is a plain,
/// ordered bag of named values (the same "dynamic row" shape most real reporting engines use) — genuinely
/// filterable/sortable/pageable/exportable through one pipeline, matching §42.9's own rules
/// (run time/filters/user in the export header) without 15 bespoke result types.</summary>
public sealed class ReportFilter
{
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public DateOnly? AsOf { get; set; }
    public int? CustomerId { get; set; }
    public long? InvoiceId { get; set; }
    public int? VehicleId { get; set; }
    public int? DriverId { get; set; }
    public int? RouteId { get; set; }
    public long? TripConfigurationId { get; set; }
    public string? Method { get; set; }
    public long? BankCashAccountId { get; set; }
    public string? SettlementType { get; set; }
    public string? AdvanceStatus { get; set; }
    /// <summary>CC-42's own common filters (§42's own "Common filters" list): trip type, trip/invoice/payment
    /// status, active flag — one shared string/bool pair reused by whichever report needs it, rather than a
    /// separate named property per report's own status-like filter.</summary>
    public string? TripType { get; set; }
    public string? Status { get; set; }
    public string? PaymentStatus { get; set; }
    public bool? Active { get; set; }
    /// <summary>LED-03's own "balance sign" filter: <c>Dr</c>, <c>Cr</c>, or omitted for every customer.</summary>
    public string? BalanceSign { get; set; }
    public string? SortBy { get; set; }
    public bool SortDesc { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

/// <summary>§42.9's own background-export outcome: either the file is ready right away (small report, the
/// existing CC-41 behaviour) or a job was queued (&gt; 50,000 rows) for the caller to poll.</summary>
public sealed class ReportExportResultModel
{
    public bool Ready { get; set; }
    public byte[]? Bytes { get; set; }
    public string? FileName { get; set; }
    public long? JobId { get; set; }
    public string? Status { get; set; }
    public string? DownloadUrl { get; set; }
    public string? Error { get; set; }
}

public sealed class ReportResultModel
{
    public string Code { get; set; } = string.Empty;
    public string ReportName { get; set; } = string.Empty;
    /// <summary>§42.9: "Every report shows its run time, filters used and the user who ran it."</summary>
    public DateTime RunOn { get; set; }
    public string RunBy { get; set; } = string.Empty;
    public IReadOnlyDictionary<string, string?> FiltersUsed { get; set; } = new Dictionary<string, string?>();
    public IReadOnlyList<string> Columns { get; set; } = [];
    public PaginatedResponse<Dictionary<string, object?>> Data { get; set; } = new();
}
