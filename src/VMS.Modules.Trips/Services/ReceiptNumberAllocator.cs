using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using VMS.Modules.Trips.Data;
using VMS.Shared.Common;
using VMS.Shared.Time;

namespace VMS.Modules.Trips.Services;

/// <summary>Allocates <c>RCPT-YYYY-NNNNN</c> numbers (§37) — the same race-safe MERGE idiom as
/// <see cref="IInvoiceNumberAllocator"/>.</summary>
internal interface IReceiptNumberAllocator
{
    Task<string> NextAsync(CancellationToken ct = default);
}

internal sealed class ReceiptNumberAllocator(TripsDbContext db, ITenantContext tenant, IOperatingClock clock) : IReceiptNumberAllocator
{
    public async Task<string> NextAsync(CancellationToken ct = default)
    {
        var transaction = db.Database.CurrentTransaction
            ?? throw new InvalidOperationException("Receipt numbers must be allocated inside the transaction that saves the receipt.");

        var tenantId = tenant.TenantId;
        if (tenantId == Guid.Empty) throw new InvalidOperationException("No tenant to allocate a receipt number for.");

        var year = (await clock.TodayAsync(tenantId)).Year;
        var connection = db.Database.GetDbConnection();
        var dbTransaction = transaction.GetDbTransaction();

        await using var command = RawSql.Command(connection, dbTransaction,
            @"MERGE trp.ReceiptNumberCounters WITH (UPDLOCK, HOLDLOCK) AS c
              USING (SELECT @tenant AS TenantId, @year AS Year) AS s ON c.TenantId = s.TenantId AND c.Year = s.Year
              WHEN MATCHED THEN UPDATE SET LastNumber = c.LastNumber + 1
              WHEN NOT MATCHED THEN INSERT (TenantId, Year, LastNumber) VALUES (@tenant, @year, 1)
              OUTPUT inserted.LastNumber;",
            ("@tenant", tenantId), ("@year", year));
        var number = Convert.ToInt64(await command.ExecuteScalarAsync(ct));

        return $"RCPT-{year}-{number.ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(5, '0')}";
    }
}
