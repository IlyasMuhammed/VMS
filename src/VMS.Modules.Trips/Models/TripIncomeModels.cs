namespace VMS.Modules.Trips.Models;

public sealed class TripIncomeModel
{
    public long TripIncomeId { get; set; }
    public long TripId { get; set; }
    public int CustomerId { get; set; }
    public int IncomeTypeId { get; set; }
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public DateOnly IncomeDate { get; set; }
    public bool IsBillable { get; set; }
    public long? InvoiceLineId { get; set; }
    public string? Reference { get; set; }
    public string? Remarks { get; set; }
    public bool IsVoided { get; set; }
    public string? VoidReason { get; set; }
    public int? VoidedBy { get; set; }
    public DateTime? VoidedAtUtc { get; set; }
}

public sealed class CreateTripIncomeRequest
{
    /// <summary>Defaults to the trip's own customer (§30).</summary>
    public int? CustomerId { get; set; }
    public int IncomeTypeId { get; set; }
    public decimal Amount { get; set; }
    /// <summary>Only read when multi-currency is on; defaults to the trip's own currency otherwise (§13A).</summary>
    public string? CurrencyCode { get; set; }
    /// <summary>Defaults to today.</summary>
    public DateOnly? IncomeDate { get; set; }
    public bool IsBillable { get; set; }
    public string? Reference { get; set; }
    public string? Remarks { get; set; }
}

public sealed class VoidTripIncomeRequest
{
    public string Reason { get; set; } = string.Empty;
}
