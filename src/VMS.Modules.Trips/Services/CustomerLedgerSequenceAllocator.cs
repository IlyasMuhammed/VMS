using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using VMS.Modules.Trips.Data;
using VMS.Shared.Common;

namespace VMS.Modules.Trips.Services;

/// <summary>Allocates <see cref="Domain.CustomerLedgerEntry.CustomerSeq"/> — a monotonic, per-customer (not
/// per-year) counter, the same race-safe MERGE idiom as <see cref="ILedgerNumberAllocator"/> but with no year
/// dimension at all.</summary>
internal interface ICustomerLedgerSequenceAllocator
{
    Task<long> NextAsync(int customerId, CancellationToken ct = default);
}

internal sealed class CustomerLedgerSequenceAllocator(TripsDbContext db, ITenantContext tenant) : ICustomerLedgerSequenceAllocator
{
    public async Task<long> NextAsync(int customerId, CancellationToken ct = default)
    {
        var transaction = db.Database.CurrentTransaction
            ?? throw new InvalidOperationException("A customer ledger sequence must be allocated inside the transaction that saves the entry.");

        var tenantId = tenant.TenantId;
        if (tenantId == Guid.Empty) throw new InvalidOperationException("No tenant to allocate a ledger sequence for.");

        var connection = db.Database.GetDbConnection();
        var dbTransaction = transaction.GetDbTransaction();

        await using var command = RawSql.Command(connection, dbTransaction,
            @"MERGE trp.CustomerLedgerSequenceCounters WITH (UPDLOCK, HOLDLOCK) AS c
              USING (SELECT @tenant AS TenantId, @customerId AS CustomerId) AS s ON c.TenantId = s.TenantId AND c.CustomerId = s.CustomerId
              WHEN MATCHED THEN UPDATE SET LastSeq = c.LastSeq + 1
              WHEN NOT MATCHED THEN INSERT (TenantId, CustomerId, LastSeq) VALUES (@tenant, @customerId, 1)
              OUTPUT inserted.LastSeq;",
            ("@tenant", tenantId), ("@customerId", customerId));
        return Convert.ToInt64(await command.ExecuteScalarAsync(ct));
    }
}
