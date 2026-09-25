using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using VMS.Modules.Trips.Data;
using VMS.Shared.Common;
using VMS.Shared.Time;

namespace VMS.Modules.Trips.Services;

/// <summary>Allocates <c>LED-YYYY-NNNNNN</c> numbers (§40A.2) — the exact same shape as
/// <see cref="IInvoiceNumberAllocator"/>, just six digits instead of five (the FSD's own literal format for this
/// one series).</summary>
internal interface ILedgerNumberAllocator
{
    Task<string> NextAsync(CancellationToken ct = default);
}

internal sealed class LedgerNumberAllocator(TripsDbContext db, ITenantContext tenant, IOperatingClock clock) : ILedgerNumberAllocator
{
    public async Task<string> NextAsync(CancellationToken ct = default)
    {
        var transaction = db.Database.CurrentTransaction
            ?? throw new InvalidOperationException("Ledger entry numbers must be allocated inside the transaction that saves the entry.");

        var tenantId = tenant.TenantId;
        if (tenantId == Guid.Empty) throw new InvalidOperationException("No tenant to allocate a ledger entry number for.");

        var year = (await clock.TodayAsync(tenantId)).Year;
        var connection = db.Database.GetDbConnection();
        var dbTransaction = transaction.GetDbTransaction();

        await using var command = RawSql.Command(connection, dbTransaction,
            @"MERGE trp.LedgerNumberCounters WITH (UPDLOCK, HOLDLOCK) AS c
              USING (SELECT @tenant AS TenantId, @year AS Year) AS s ON c.TenantId = s.TenantId AND c.Year = s.Year
              WHEN MATCHED THEN UPDATE SET LastNumber = c.LastNumber + 1
              WHEN NOT MATCHED THEN INSERT (TenantId, Year, LastNumber) VALUES (@tenant, @year, 1)
              OUTPUT inserted.LastNumber;",
            ("@tenant", tenantId), ("@year", year));
        var number = Convert.ToInt64(await command.ExecuteScalarAsync(ct));

        return $"LED-{year}-{number.ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(6, '0')}";
    }
}
