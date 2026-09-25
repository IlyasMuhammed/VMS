using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using VMS.Shared.Auditing;
using VMS.Shared.Common;

namespace VMS.Modules.Trips.Data;

/// <summary>Used only by "dotnet ef" tooling to create the context without booting the API host.</summary>
internal sealed class TripsDbContextFactory : IDesignTimeDbContextFactory<TripsDbContext>
{
    public TripsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<TripsDbContext>()
            .UseSqlServer(DesignTimeConnection.Resolve(), sql =>
                sql.MigrationsHistoryTable("__EFMigrationsHistory", TripsDbContext.Schema))
            .Options;

        return new TripsDbContext(options, new StaticTenantContext(), NoAuditContext.Instance);
    }
}
