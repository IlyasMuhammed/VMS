using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using VMS.Modules.Core.Data;
using VMS.Modules.Core.Domain;
using VMS.Modules.Core.Models;
using VMS.Shared.Common;
using VMS.Shared.Exceptions;
using VMS.Shared.Numbering;
using VMS.Shared.Time;

namespace VMS.Modules.Core.Services;

public interface INumberSeriesAdminService
{
    Task<List<NumberSeriesModel>> ListAsync();
    Task<NumberSeriesModel> UpdateAsync(string code, UpdateNumberSeriesRequest request);
}

/// <summary>The series master the client edits under Administration (FSD §24.1).</summary>
internal sealed partial class NumberSeriesAdminService(CoreDbContext db, ITenantContext tenantContext, IOperatingClock clock) : INumberSeriesAdminService
{
    public const int MinPadding = 3;
    public const int MaxPadding = 10;

    [GeneratedRegex("^[A-Z0-9]{1,10}$")]
    private static partial Regex PrefixPattern();

    public async Task<List<NumberSeriesModel>> ListAsync()
    {
        await EnsureAllAsync();
        // A Super Admin's query filter spans every tenant; this list is always the signed-in tenant's own.
        var rows = await db.NumberSeries.AsNoTracking().Where(s => s.TenantId == tenantContext.TenantId).ToListAsync();
        var today = await clock.TodayAsync(tenantContext.TenantId);
        var counters = await CurrentCountersAsync(rows, today);

        return NumberSeriesCodes.Defaults
            .Select(d => (Default: d, Row: rows.FirstOrDefault(r => r.Code == d.Code)))
            .Where(x => x.Row is not null)
            .Select(x => ToModel(x.Default, x.Row!, counters, today))
            .ToList();
    }

    public async Task<NumberSeriesModel> UpdateAsync(string code, UpdateNumberSeriesRequest request)
    {
        var known = NumberSeriesCodes.Defaults.FirstOrDefault(d => d.Code == code)
            ?? throw new NotFoundException("Numbering series not found.");

        var prefix = (request.Prefix ?? string.Empty).Trim().ToUpperInvariant();
        if (!PrefixPattern().IsMatch(prefix))
            throw new BadRequestException("The prefix must be 1 to 10 letters or digits.");
        if (request.Padding is < MinPadding or > MaxPadding)
            throw new BadRequestException($"The number must have between {MinPadding} and {MaxPadding} digits.");
        if (!ResetPeriods.All.Contains(request.ResetPeriod))
            throw new BadRequestException($"Reset must be one of: {string.Join(", ", ResetPeriods.All)}.");

        await EnsureAllAsync();
        var row = await db.NumberSeries.SingleAsync(s => s.Code == code && s.TenantId == tenantContext.TenantId);
        row.Prefix = prefix;
        row.Padding = request.Padding;
        row.ResetPeriod = request.ResetPeriod;
        await db.SaveChangesAsync();   // audited: the old and new prefix, digits and reset are on record

        var today = await clock.TodayAsync(tenantContext.TenantId);
        return ToModel(known, row, await CurrentCountersAsync([row], today), today);
    }

    private async Task EnsureAllAsync()
    {
        var connection = db.Database.GetDbConnection();
        var wasClosed = connection.State != System.Data.ConnectionState.Open;
        if (wasClosed) await connection.OpenAsync();
        try
        {
            foreach (var series in NumberSeriesCodes.Defaults)
                await NumberSeriesSql.EnsureAsync(connection, db.Database.CurrentTransaction?.GetDbTransaction(), tenantContext.TenantId, series, default);
        }
        finally
        {
            if (wasClosed) await connection.CloseAsync();
        }
    }

    private async Task<Dictionary<int, long>> CurrentCountersAsync(IReadOnlyCollection<NumberSeries> rows, DateOnly today)
    {
        var result = new Dictionary<int, long>();
        foreach (var row in rows)
        {
            var period = NumberFormat.PeriodKey(row.ResetPeriod, today);
            result[row.NumberSeriesID] = await db.NumberSeriesCounters
                .Where(c => c.NumberSeriesID == row.NumberSeriesID && c.PeriodKey == period)
                .Select(c => c.LastNumber).FirstOrDefaultAsync();
        }
        return result;
    }

    private static NumberSeriesModel ToModel(NumberSeriesDefault known, NumberSeries row, IReadOnlyDictionary<int, long> last, DateOnly today) => new()
    {
        Code = row.Code,
        Entity = known.Entity,
        Prefix = row.Prefix,
        Padding = row.Padding,
        ResetPeriod = row.ResetPeriod,
        NextNumber = NumberFormat.Format(row.Prefix, row.Padding, row.ResetPeriod, today, last.GetValueOrDefault(row.NumberSeriesID) + 1)
    };
}
