using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using VMS.Modules.Trips.Data;
using VMS.Modules.Trips.Domain;
using VMS.Shared.Common;

namespace VMS.Modules.Trips.Services;

/// <summary>
/// Creates a tenant's PKR base currency and its currency settings row the first time either is needed — the same
/// lazy-per-tenant idiom as every other master list in this repo (<c>PlatformLookups</c>, notification rule
/// defaults, the tenant's first "Head Office" branch): race-safe raw SQL, never re-run once a row exists, never
/// overwrites a tenant's own edits.
/// </summary>
internal interface ICurrencySeeder
{
    Task EnsureSeededAsync(CancellationToken ct = default);
}

internal sealed class CurrencySeeder(TripsDbContext db, ITenantContext tenant) : ICurrencySeeder
{
    public async Task EnsureSeededAsync(CancellationToken ct = default)
    {
        var tenantId = tenant.TenantId;
        if (tenantId == Guid.Empty) throw new InvalidOperationException("No tenant to seed currencies for.");

        var connection = db.Database.GetDbConnection();
        var wasClosed = connection.State != System.Data.ConnectionState.Open;
        if (wasClosed) await connection.OpenAsync(ct);
        try
        {
            var transaction = db.Database.CurrentTransaction?.GetDbTransaction();

            await using (var check = RawSql.Command(connection, transaction,
                             "SELECT CASE WHEN EXISTS (SELECT 1 FROM trp.CurrencySettings WHERE TenantId = @tenant) THEN 1 ELSE 0 END",
                             ("@tenant", tenantId)))
            {
                if (Convert.ToInt32(await check.ExecuteScalarAsync(ct)) == 1) return;
            }

            await using var insert = RawSql.Command(connection, transaction,
                @"SET XACT_ABORT ON;
                  BEGIN TRAN;
                  IF NOT EXISTS (SELECT 1 FROM trp.CurrencySettings WITH (UPDLOCK, HOLDLOCK) WHERE TenantId = @tenant)
                  BEGIN
                      INSERT INTO trp.Currencies (TenantId, CurrencyCode, CurrencyName, Symbol, DecimalPlaces, IsBase, Status)
                      VALUES (@tenant, 'PKR', 'Pakistani Rupee', N'Rs', 2, 1, 'Active');
                      INSERT INTO trp.CurrencySettings (TenantId, BaseCurrencyCode, MultiCurrencyEnabled)
                      VALUES (@tenant, 'PKR', 0);
                  END
                  COMMIT;",
                ("@tenant", tenantId));
            await insert.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            if (wasClosed) await connection.CloseAsync();
        }
    }
}
