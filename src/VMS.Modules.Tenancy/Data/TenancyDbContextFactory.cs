using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using VMS.Shared.Common;

namespace VMS.Modules.Tenancy.Data;

/// <summary>Used only by "dotnet ef" tooling to create the context without booting the API host.</summary>
internal sealed class TenancyDbContextFactory : IDesignTimeDbContextFactory<TenancyDbContext>
{
    public TenancyDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<TenancyDbContext>()
            .UseSqlServer(DesignTimeConnection.Resolve(), sql =>
                sql.MigrationsHistoryTable("__EFMigrationsHistory", TenancyDbContext.Schema))
            .Options;

        return new TenancyDbContext(options);
    }
}
