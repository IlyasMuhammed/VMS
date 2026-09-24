using VMS.Modules.Vehicles.Domain;

namespace VMS.Modules.Vehicles.Services;

/// <summary>
/// The date math of a recurring charge (FSD §19A.1): when its first occurrence falls, and when the one after any occurrence falls.
/// Pure, like <see cref="ScheduleGenerator"/>, so the generation job's timing can be tested without a database.
/// </summary>
public static class ChargeSchedule
{
    /// <summary>The first occurrence on or after <paramref name="from"/> (normally the charge's start date).</summary>
    public static DateOnly FirstDue(DateOnly from, string frequency, int? dueDay, int? dueMonth) => frequency switch
    {
        ChargeFrequencies.Weekly or ChargeFrequencies.CustomDays => from,
        ChargeFrequencies.Yearly => FirstYearly(from, dueMonth ?? from.Month, dueDay ?? from.Day),
        _ => FirstByMonths(from, ChargeFrequencies.Months(frequency) ?? 1, dueDay ?? from.Day),
    };

    /// <summary>The occurrence after <paramref name="current"/>.</summary>
    public static DateOnly Next(DateOnly current, string frequency, int? dueDay, int? customIntervalDays) => frequency switch
    {
        ChargeFrequencies.Weekly => current.AddDays(7),
        ChargeFrequencies.CustomDays => current.AddDays(Math.Max(1, customIntervalDays ?? 30)),
        ChargeFrequencies.Yearly => DateForMonth(current.Year + 1, current.Month, dueDay ?? current.Day),
        _ => StepMonths(current, ChargeFrequencies.Months(frequency) ?? 1, dueDay ?? current.Day),
    };

    /// <summary>The idempotency key of an occurrence (BR-VH-030): a calendar period for the regular frequencies, the date itself for Weekly and Custom Days, where a period alone would not be unique.</summary>
    public static string PeriodKeyOf(DateOnly due, string frequency) => frequency switch
    {
        ChargeFrequencies.Weekly or ChargeFrequencies.CustomDays => due.ToString("yyyy-MM-dd"),
        ChargeFrequencies.Yearly => due.Year.ToString(),
        _ => due.ToString("yyyy-MM"),
    };

    private static DateOnly FirstByMonths(DateOnly from, int months, int day)
    {
        var candidate = DateForMonth(from.Year, from.Month, day);
        return candidate >= from ? candidate : StepMonths(candidate, months, day);
    }

    private static DateOnly FirstYearly(DateOnly from, int month, int day)
    {
        var candidate = DateForMonth(from.Year, month, day);
        return candidate >= from ? candidate : DateForMonth(from.Year + 1, month, day);
    }

    private static DateOnly StepMonths(DateOnly current, int months, int day)
    {
        var total = current.Year * 12 + (current.Month - 1) + months;
        return DateForMonth(total / 12, total % 12 + 1, day);
    }

    /// <summary>29 to 31 falls back to the last day of a shorter month, the same rule the finance schedule uses.</summary>
    private static DateOnly DateForMonth(int year, int month, int day) => new(year, month, Math.Min(day, DateTime.DaysInMonth(year, month)));
}
