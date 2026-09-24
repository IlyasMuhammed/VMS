using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VMS.Shared.Common;

namespace VMS.Shared.Time;

/// <summary>
/// "What day is it for this business?" (NFR-DT-06). A batch or a number series that decides by the
/// calendar date must use the company's operating time zone, not the server's and not the user's, or a
/// reminder fires on different days for different people and a number takes the wrong year for a few hours
/// around New Year. The zone is the tenant's <c>TimeZone</c>; the platform default is Asia/Karachi
/// (<c>Operations:DefaultTimeZone</c>), pending OQ-11.
/// </summary>
public interface IOperatingClock
{
    string DefaultTimeZoneId { get; }

    /// <summary>The current calendar date in the tenant's operating time zone.</summary>
    Task<DateOnly> TodayAsync(Guid tenantId);

    /// <summary>The current calendar date in a named IANA zone; an unknown or empty name uses the default zone.</summary>
    DateOnly Today(string? timeZoneId);
}

internal sealed class OperatingClock(TimeProvider time, ITenantSnapshotProvider tenants, IConfiguration configuration) : IOperatingClock
{
    public string DefaultTimeZoneId { get; } = configuration["Operations:DefaultTimeZone"] is { Length: > 0 } configured ? configured : "Asia/Karachi";

    public async Task<DateOnly> TodayAsync(Guid tenantId) =>
        Today((await tenants.GetSnapshotAsync(tenantId))?.TimeZone);

    public DateOnly Today(string? timeZoneId)
    {
        var zone = Resolve(timeZoneId) ?? Resolve(DefaultTimeZoneId) ?? TimeZoneInfo.Utc;
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), zone).DateTime);
    }

    private static TimeZoneInfo? Resolve(string? id) =>
        !string.IsNullOrWhiteSpace(id) && TimeZoneInfo.TryFindSystemTimeZoneById(id.Trim(), out var zone) ? zone : null;
}

/// <summary>
/// The time zone of the person making the request, as their browser reports it (<c>X-Time-Zone</c>, an
/// IANA name such as <c>Asia/Karachi</c>). The server never formats dates for the screen (NFR-DT-05), but an
/// export or a printout is built here and must show the exporting user's own zone (NFR-DT-07).
/// </summary>
public interface IClientTimeZone
{
    /// <summary>The zone the browser reported, or null if it sent none or an unknown one.</summary>
    TimeZoneInfo? Current { get; }
}

internal sealed class ClientTimeZone(IHttpContextAccessor accessor) : IClientTimeZone
{
    public const string HeaderName = "X-Time-Zone";

    public TimeZoneInfo? Current
    {
        get
        {
            var value = accessor.HttpContext?.Request.Headers[HeaderName].ToString();
            return value is { Length: > 0 and <= 64 } && TimeZoneInfo.TryFindSystemTimeZoneById(value, out var zone) ? zone : null;
        }
    }
}

public static class TimeServiceExtensions
{
    /// <summary>Registered once, centrally, before any module.</summary>
    public static IServiceCollection AddPlatformTime(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IOperatingClock, OperatingClock>();
        services.AddScoped<IClientTimeZone, ClientTimeZone>();
        return services;
    }
}
