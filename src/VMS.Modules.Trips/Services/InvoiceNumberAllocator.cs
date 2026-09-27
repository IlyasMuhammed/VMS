using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using VMS.Modules.Trips.Data;
using VMS.Shared.Common;
using VMS.Shared.Time;

namespace VMS.Modules.Trips.Services;

/// <summary>Allocates <c>INV-YYYY-MM-NNNN</c> numbers (§32.4, §46.6, §58 #9 — Q1's answer: "INV-2026-01-0001," a
/// 4-digit sequence resetting every calendar month, no per-customer prefix). CC-25's own temporary
/// `INV-YYYY-NNNNN` allocator is the seam this task swaps in behind, without touching
/// <see cref="InvoiceCreationService"/> at all.</summary>
internal interface IInvoiceNumberAllocator
{
    Task<string> NextAsync(CancellationToken ct = default);
}

internal sealed class InvoiceNumberAllocator(TripsDbContext db, ITenantContext tenant, IOperatingClock clock) : IInvoiceNumberAllocator
{
    public async Task<string> NextAsync(CancellationToken ct = default)
    {
        var transaction = db.Database.CurrentTransaction
            ?? throw new InvalidOperationException("Invoice numbers must be allocated inside the transaction that saves the invoice.");

        var tenantId = tenant.TenantId;
        if (tenantId == Guid.Empty) throw new InvalidOperationException("No tenant to allocate an invoice number for.");

        var today = await clock.TodayAsync(tenantId);
        var year = today.Year;
        var month = today.Month;
        var connection = db.Database.GetDbConnection();
        var dbTransaction = transaction.GetDbTransaction();

        await using var command = RawSql.Command(connection, dbTransaction,
            @"MERGE trp.InvoiceNumberCounters WITH (UPDLOCK, HOLDLOCK) AS c
              USING (SELECT @tenant AS TenantId, @year AS Year, @month AS Month) AS s ON c.TenantId = s.TenantId AND c.Year = s.Year AND c.Month = s.Month
              WHEN MATCHED THEN UPDATE SET LastNumber = c.LastNumber + 1
              WHEN NOT MATCHED THEN INSERT (TenantId, Year, Month, LastNumber) VALUES (@tenant, @year, @month, 1)
              OUTPUT inserted.LastNumber;",
            ("@tenant", tenantId), ("@year", year), ("@month", month));
        var number = Convert.ToInt64(await command.ExecuteScalarAsync(ct));

        return $"INV-{year}-{month.ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(2, '0')}-{number.ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(4, '0')}";
    }
}
