using VMS.Modules.Vehicles.Domain;

namespace VMS.Modules.Vehicles.Services;

/// <summary>One expected payment of a generated schedule.</summary>
public sealed record ScheduleRow(int InstallmentNo, DateOnly DueDate, decimal Amount, bool IsResidual);

/// <summary>
/// Generates the installment schedule of an agreement (FSD §18.3, FR-VH-004): one row per period from the first due date, stepping by the
/// frequency, and a final row for the residual (balloon) amount if there is one. Installments are a single amount each (OQ-07): the
/// markup rate is kept for reference and never splits a row.
/// </summary>
public static class ScheduleGenerator
{
    /// <summary>
    /// Each due date is worked out from the first one, not from the one before, so an agreement starting on the 31st falls on the
    /// last day of a shorter month and comes back to the 31st after it (31 Jan, 28 Feb, 31 Mar), and 29 to 31 never drift.
    /// The residual is due with the last installment, at the end of the tenure, and is numbered after it.
    /// </summary>
    public static IReadOnlyList<ScheduleRow> Generate(DateOnly firstDue, string frequency, int tenure, decimal installment, decimal? residual)
    {
        var step = FinanceFrequencies.Months(frequency);
        var rows = new List<ScheduleRow>(tenure + 1);
        for (var n = 0; n < tenure; n++) rows.Add(new ScheduleRow(n + 1, firstDue.AddMonths(n * step), installment, false));
        if (residual is > 0) rows.Add(new ScheduleRow(tenure + 1, rows[^1].DueDate, residual.Value, true));
        return rows;
    }

    /// <summary>Applies due dates the person corrected on the preview, for a bank whose schedule is not regular (FR-VH-005). The rest keep their generated date.</summary>
    public static IReadOnlyList<ScheduleRow> WithDueDates(IReadOnlyList<ScheduleRow> rows, IReadOnlyDictionary<int, DateOnly> changed) =>
        rows.Select(r => changed.TryGetValue(r.InstallmentNo, out var due) ? r with { DueDate = due } : r).ToList();

    /// <summary>The dates must not go backwards from one installment to the next, and none may be before the agreement was made.</summary>
    public static string? DateProblem(IReadOnlyList<ScheduleRow> rows, DateOnly agreementDate, out int installmentNo)
    {
        installmentNo = 0;
        DateOnly? previous = null;
        foreach (var row in rows)
        {
            if (row.DueDate < agreementDate) { installmentNo = row.InstallmentNo; return "before"; }
            if (previous is { } p && row.DueDate < p) { installmentNo = row.InstallmentNo; return "order"; }
            previous = row.DueDate;
        }
        return null;
    }
}
