using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using VMS.Modules.Core.Data;
using VMS.Shared.Common;
using VMS.Shared.Numbering;
using VMS.Shared.Time;

namespace VMS.Modules.Core.Services;

/// <summary>
/// Allocates numbers on the caller's own connection and transaction, so the increment commits or rolls
/// back with the record it numbers. The increment takes an update lock on the counter row that is held
/// until that transaction ends: the next save for the same series waits, and gets the next number only
/// once this one has committed or returned its number by rolling back. That wait is the price of having
/// no gaps.
/// </summary>
internal sealed class NumberSeriesAllocator(ITenantContext tenantContext, IOperatingClock clock) : INumberSeries
{
    public async Task<string> NextAsync(DbContext db, string seriesCode, DateOnly? date = null, Guid? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        var transaction = db.Database.CurrentTransaction
            ?? throw new InvalidOperationException(
                "Numbers must be allocated inside the transaction that saves the record. Wrap the save in db.InTransactionAsync(...); " +
                "a number taken outside it would be lost, leaving a gap, if the save failed.");

        var tenant = tenantId ?? tenantContext.TenantId;
        if (tenant == Guid.Empty) throw new InvalidOperationException("No tenant to allocate a number for.");

        var fallback = NumberSeriesCodes.Defaults.FirstOrDefault(d => d.Code == seriesCode)
            ?? throw new InvalidOperationException($"Unknown numbering series '{seriesCode}'.");

        var connection = db.Database.GetDbConnection();
        var dbTransaction = transaction.GetDbTransaction();
        // With no date given, today is the tenant's own calendar day, so the year does not turn over
        // hours early or late for the company (NFR-DT-06).
        var day = date ?? await clock.TodayAsync(tenant);

        await NumberSeriesSql.EnsureAsync(connection, dbTransaction, tenant, fallback, cancellationToken);
        var series = await ReadAsync(connection, dbTransaction, tenant, seriesCode, cancellationToken);

        var period = NumberFormat.PeriodKey(series.ResetPeriod, day);
        var number = await IncrementAsync(connection, dbTransaction, series.Id, period, cancellationToken);

        return NumberFormat.Format(series.Prefix, series.Padding, series.ResetPeriod, day, number);
    }

    private sealed record Row(int Id, string Prefix, int Padding, string ResetPeriod);

    private static async Task<Row> ReadAsync(DbConnection connection, DbTransaction transaction, Guid tenant, string code, CancellationToken ct)
    {
        await using var command = NumberSeriesSql.Command(connection, transaction,
            "SELECT NumberSeriesID, Prefix, Padding, ResetPeriod FROM core.NumberSeries WHERE TenantId = @tenant AND Code = @code",
            ("@tenant", tenant), ("@code", code));
        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return new Row(reader.GetInt32(0), reader.GetString(1), reader.GetInt32(2), reader.GetString(3));
    }

    /// <summary>One statement that either bumps the period's counter or starts it at 1, safe against two callers racing to start it.</summary>
    private static async Task<long> IncrementAsync(DbConnection connection, DbTransaction transaction, int seriesId, string period, CancellationToken ct)
    {
        await using var command = NumberSeriesSql.Command(connection, transaction,
            @"MERGE core.NumberSeriesCounters WITH (UPDLOCK, HOLDLOCK) AS c
              USING (SELECT @series AS NumberSeriesID, @period AS PeriodKey) AS s
                 ON c.NumberSeriesID = s.NumberSeriesID AND c.PeriodKey = s.PeriodKey
              WHEN MATCHED THEN UPDATE SET LastNumber = c.LastNumber + 1
              WHEN NOT MATCHED THEN INSERT (NumberSeriesID, PeriodKey, LastNumber) VALUES (@series, @period, 1)
              OUTPUT inserted.LastNumber;",
            ("@series", seriesId), ("@period", period));
        return Convert.ToInt64(await command.ExecuteScalarAsync(ct));
    }
}

internal static class NumberSeriesSql
{
    public static DbCommand Command(DbConnection connection, DbTransaction? transaction, string sql, params (string Name, object Value)[] parameters) =>
        RawSql.Command(connection, transaction, sql, parameters.Select(p => (p.Name, (object?)p.Value)).ToArray());

    /// <summary>
    /// Creates the tenant's row for a series from its default the first time it is needed. The plain
    /// existence check comes first so the usual case takes no lock on the row; the MERGE only runs, and
    /// only locks, when it may have to insert, and is safe if two callers get there together.
    /// </summary>
    public static async Task EnsureAsync(DbConnection connection, DbTransaction? transaction, Guid tenant, NumberSeriesDefault series, CancellationToken ct)
    {
        await using var command = Command(connection, transaction,
            @"IF NOT EXISTS (SELECT 1 FROM core.NumberSeries WHERE TenantId = @tenant AND Code = @code)
                MERGE core.NumberSeries WITH (UPDLOCK, HOLDLOCK) AS t
                USING (SELECT @tenant AS TenantId, @code AS Code) AS s ON t.TenantId = s.TenantId AND t.Code = s.Code
                WHEN NOT MATCHED THEN INSERT (TenantId, Code, Prefix, Padding, ResetPeriod) VALUES (@tenant, @code, @prefix, @padding, @reset);",
            ("@tenant", tenant), ("@code", series.Code), ("@prefix", series.Prefix), ("@padding", series.Padding), ("@reset", series.ResetPeriod));
        await command.ExecuteNonQueryAsync(ct);
    }
}
