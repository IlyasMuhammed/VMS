using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using VMS.Modules.Trips.Data;
using VMS.Shared.Common;
using VMS.Shared.Time;

namespace VMS.Modules.Trips.Services;

/// <summary>Allocates <c>TRP-YYYY-NNNNNN</c> numbers (see <see cref="Domain.TripNumberCounter"/> for why this is
/// not the shared <c>INumberSeries</c>). Gap-free the same way: must run inside the transaction that saves the
/// trip, so a failed save never burns a number.</summary>
internal interface ITripNumberAllocator
{
    Task<string> NextAsync(CancellationToken ct = default);
}

internal sealed class TripNumberAllocator(TripsDbContext db, ITenantContext tenant, IOperatingClock clock) : ITripNumberAllocator
{
    public async Task<string> NextAsync(CancellationToken ct = default)
    {
        var transaction = db.Database.CurrentTransaction
            ?? throw new InvalidOperationException("Trip numbers must be allocated inside the transaction that saves the trip.");

        var tenantId = tenant.TenantId;
        if (tenantId == Guid.Empty) throw new InvalidOperationException("No tenant to allocate a trip number for.");

        var year = (await clock.TodayAsync(tenantId)).Year;
        var connection = db.Database.GetDbConnection();
        var dbTransaction = transaction.GetDbTransaction();

        await using var command = RawSql.Command(connection, dbTransaction,
            @"MERGE trp.TripNumberCounters WITH (UPDLOCK, HOLDLOCK) AS c
              USING (SELECT @tenant AS TenantId, @year AS Year) AS s ON c.TenantId = s.TenantId AND c.Year = s.Year
              WHEN MATCHED THEN UPDATE SET LastNumber = c.LastNumber + 1
              WHEN NOT MATCHED THEN INSERT (TenantId, Year, LastNumber) VALUES (@tenant, @year, 1)
              OUTPUT inserted.LastNumber;",
            ("@tenant", tenantId), ("@year", year));
        var number = Convert.ToInt64(await command.ExecuteScalarAsync(ct));

        return $"TRP-{year}-{number.ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(6, '0')}";
    }
}
