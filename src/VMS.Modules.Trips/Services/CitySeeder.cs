using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using VMS.Modules.Trips.Data;
using VMS.Shared.Common;
using VMS.Shared.Lookups;

namespace VMS.Modules.Trips.Services;

/// <summary>Seeds §16's own starting list (Lahore LHR, Islamabad ISL, Faisalabad FSD, Sheikhupura SKP, Multan MUL,
/// Karachi KHI, Dera Ghazi Khan DGK) the first time a tenant's city list is touched — the same lazy-per-tenant,
/// race-safe idiom as <see cref="CurrencySeeder"/> and every other master list in this repo.</summary>
internal interface ICitySeeder
{
    Task EnsureSeededAsync(CancellationToken ct = default);
}

internal sealed class CitySeeder(TripsDbContext db, ITenantContext tenant, ILookupReader lookups) : ICitySeeder
{
    private static readonly (string Name, string Abbr, string Province)[] Defaults =
    [
        ("Lahore", "LHR", "Punjab"), ("Islamabad", "ISL", "Islamabad Capital Territory"), ("Faisalabad", "FSD", "Punjab"),
        ("Sheikhupura", "SKP", "Punjab"), ("Multan", "MUL", "Punjab"), ("Karachi", "KHI", "Sindh"), ("Dera Ghazi Khan", "DGK", "Punjab"),
    ];

    public async Task EnsureSeededAsync(CancellationToken ct = default)
    {
        var tenantId = tenant.TenantId;
        if (tenantId == Guid.Empty) throw new InvalidOperationException("No tenant to seed cities for.");

        var connection = db.Database.GetDbConnection();
        var wasClosed = connection.State != System.Data.ConnectionState.Open;
        if (wasClosed) await connection.OpenAsync(ct);
        try
        {
            var transaction = db.Database.CurrentTransaction?.GetDbTransaction();

            await using (var check = RawSql.Command(connection, transaction,
                             "SELECT CASE WHEN EXISTS (SELECT 1 FROM trp.Cities WHERE TenantId = @tenant) THEN 1 ELSE 0 END",
                             ("@tenant", tenantId)))
            {
                if (Convert.ToInt32(await check.ExecuteScalarAsync(ct)) == 1) return;
            }

            var pakistan = (await lookups.GetActiveAsync(PlatformLookups.Country)).FirstOrDefault(c => c.Code == "PAKISTAN");
            if (pakistan is null) return;   // nothing to seed against — extremely unlikely, the Country lookup seeds itself first

            var parameters = new List<(string, object?)> { ("@tenant", tenantId), ("@country", pakistan.Id) };
            var rows = new List<string>();
            for (var i = 0; i < Defaults.Length; i++)
            {
                var (name, abbr, province) = Defaults[i];
                parameters.Add(($"@n{i}", name));
                parameters.Add(($"@a{i}", abbr));
                parameters.Add(($"@p{i}", province));
                rows.Add($"(@tenant, @n{i}, @a{i}, @country, @p{i}, 'Active')");
            }

            await using var insert = RawSql.Command(connection, transaction,
                $@"SET XACT_ABORT ON;
                   BEGIN TRAN;
                   IF NOT EXISTS (SELECT 1 FROM trp.Cities WITH (UPDLOCK, HOLDLOCK) WHERE TenantId = @tenant)
                       INSERT INTO trp.Cities (TenantId, CityName, Abbreviation, CountryId, ProvinceState, Status)
                       VALUES {string.Join(", ", rows)};
                   COMMIT;",
                parameters.ToArray());
            await insert.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            if (wasClosed) await connection.CloseAsync();
        }
    }
}
