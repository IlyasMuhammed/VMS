using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using VMS.Shared.Auditing;
using VMS.Shared.Common;

namespace VMS.Modules.Vehicles.Data;

/// <summary>Used only by "dotnet ef" tooling to create the context without booting the API host.</summary>
internal sealed class VehicleDbContextFactory : IDesignTimeDbContextFactory<VehicleDbContext>
{
    public VehicleDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<VehicleDbContext>()
            .UseSqlServer(DesignTimeConnection.Resolve(), sql =>
                sql.MigrationsHistoryTable("__EFMigrationsHistory", VehicleDbContext.Schema))
            .Options;

        return new VehicleDbContext(options, new StaticTenantContext(), NoAuditContext.Instance);
    }
}
