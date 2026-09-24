using VMS.Modules.Auth.Data;
using VMS.Modules.Auth.Domain;
using VMS.Shared.Authorization;

namespace VMS.Modules.Auth.Services;

/// <summary>Writes refused requests to <c>auth.AccessDenials</c> so repeated denials can be reported.</summary>
internal sealed class EfAccessDenialSink(AuthDbContext db) : IAccessDenialSink
{
    public async Task RecordAsync(AccessDenial denial)
    {
        db.AccessDenials.Add(new AccessDenialRecord
        {
            OccurredAt = denial.OccurredAt,
            TenantId = denial.TenantId,
            UserId = denial.UserId,
            UserName = Truncate(denial.UserName, 200),
            Permission = Truncate(denial.Permission, 100)!,
            Method = Truncate(denial.Method, 10)!,
            Path = Truncate(denial.Path, 500)!,
            RouteValues = Truncate(denial.RouteValues, 500),
            IpAddress = Truncate(denial.IpAddress, 64)
        });
        await db.SaveChangesAsync();
    }

    private static string? Truncate(string? value, int max) => value is { Length: var n } && n > max ? value[..max] : value;
}
