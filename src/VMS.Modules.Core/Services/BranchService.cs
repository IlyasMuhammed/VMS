using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using VMS.Modules.Core.Data;
using VMS.Modules.Core.Domain;
using VMS.Modules.Core.Models;
using VMS.Shared.Branches;
using VMS.Shared.Common;

namespace VMS.Modules.Core.Services;

public interface IBranchService
{
    /// <summary>The tenant's branches, active and retired, in name order. Creates the tenant's one starting branch the first time it is asked for.</summary>
    Task<List<BranchModel>> ListAsync();
}

/// <summary>
/// The tenant's own locations (FSD §6 field 15). A tenant starts with one, "Head Office" (OQ-10: one branch for now), created lazily
/// the first time anything asks — the same starting-from-nothing idiom <see cref="LookupService"/> uses for the platform lists, so a
/// tenant needs no setup step before a partner or a vehicle can be given a branch.
/// </summary>
internal sealed class BranchService(CoreDbContext db, ITenantContext tenantContext) : IBranchService, IBranchDirectory
{
    private const string HeadOfficeCode = "HEAD_OFFICE";
    private Guid TenantId => tenantContext.TenantId;

    public async Task<List<BranchModel>> ListAsync()
    {
        await EnsureStartingBranchAsync();
        var tenant = TenantId;
        var rows = await db.Branches.AsNoTracking().Where(b => b.TenantId == tenant).OrderBy(b => b.Name).ToListAsync();
        return rows.Select(ToModel).ToList();
    }

    public async Task<BranchInfo?> FindAsync(Guid branchId, CancellationToken cancellationToken = default)
    {
        await EnsureStartingBranchAsync();
        var tenant = TenantId;
        var row = await db.Branches.AsNoTracking().FirstOrDefaultAsync(b => b.TenantId == tenant && b.BranchId == branchId, cancellationToken);
        return row is null ? null : ToInfo(row);
    }

    public async Task<IReadOnlyDictionary<Guid, BranchInfo>> FindManyAsync(IEnumerable<Guid> branchIds, CancellationToken cancellationToken = default)
    {
        var wanted = branchIds.Distinct().ToList();
        if (wanted.Count == 0) return new Dictionary<Guid, BranchInfo>();
        var tenant = TenantId;
        var rows = await db.Branches.AsNoTracking().Where(b => b.TenantId == tenant && wanted.Contains(b.BranchId)).ToListAsync(cancellationToken);
        return rows.ToDictionary(b => b.BranchId, ToInfo);
    }

    private static BranchInfo ToInfo(Branch b) => new(b.BranchId, b.Code, b.Name, b.IsActive);

    private static BranchModel ToModel(Branch b) => new(b.BranchId, b.Code, b.Name, b.IsActive);

    /// <summary>
    /// Creates "Head Office" once, the same way <see cref="LookupService.EnsureDefaultsAsync"/> starts a lookup list: a tenant that
    /// already has a branch is left alone (a name or a code the tenant changed is never put back), and two requests arriving
    /// together do not create two branches. Written with SQL rather than the change tracker, so it is not logged as if a person typed it.
    /// </summary>
    private async Task EnsureStartingBranchAsync()
    {
        var tenant = TenantId;
        if (tenant == Guid.Empty) throw new InvalidOperationException("No tenant to prepare a branch for.");

        var connection = db.Database.GetDbConnection();
        var wasClosed = connection.State != System.Data.ConnectionState.Open;
        if (wasClosed) await connection.OpenAsync();
        try
        {
            var transaction = db.Database.CurrentTransaction?.GetDbTransaction();

            await using (var check = RawSql.Command(connection, transaction,
                             "SELECT CASE WHEN EXISTS (SELECT 1 FROM core.Branches WHERE TenantId = @tenant) THEN 1 ELSE 0 END", ("@tenant", tenant)))
            {
                if (Convert.ToInt32(await check.ExecuteScalarAsync()) == 1) return;
            }

            await using var insert = RawSql.Command(connection, transaction,
                @"SET XACT_ABORT ON;
                  BEGIN TRAN;
                  IF NOT EXISTS (SELECT 1 FROM core.Branches WITH (UPDLOCK, HOLDLOCK) WHERE TenantId = @tenant)
                      INSERT INTO core.Branches (BranchId, TenantId, Code, Name, IsActive, CreatedOn)
                      VALUES (@id, @tenant, @code, @name, 1, SYSUTCDATETIME());
                  COMMIT;",
                ("@id", Guid.NewGuid()), ("@tenant", tenant), ("@code", HeadOfficeCode), ("@name", "Head Office"));
            await insert.ExecuteNonQueryAsync();
        }
        finally
        {
            if (wasClosed) await connection.CloseAsync();
        }
    }
}
