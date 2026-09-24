using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace VMS.Shared.Time;

/// <summary>
/// Makes every <see cref="DateTime"/> column hold UTC and come back marked as UTC (NFR-DT-01). A value
/// handed in as local time is converted on the way in; one read from the database is stamped
/// <see cref="DateTimeKind.Utc"/> instead of the provider's default of unspecified, so it can never be
/// mistaken for local time further along. Business dates stay <see cref="DateOnly"/> (a <c>date</c> column).
/// Call it from <c>ConfigureConventions</c> of every DbContext.
/// </summary>
public static class UtcDateTimeConvention
{
    public static ModelConfigurationBuilder UseUtcDateTimes(this ModelConfigurationBuilder configuration)
    {
        configuration.Properties<DateTime>().HaveConversion<UtcConverter>();
        return configuration;
    }

    private sealed class UtcConverter() : ValueConverter<DateTime, DateTime>(
        v => v.Kind == DateTimeKind.Local ? v.ToUniversalTime() : DateTime.SpecifyKind(v, DateTimeKind.Utc),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
}
