using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using VMS.Shared.Auditing;
using VMS.Shared.Common;

namespace VMS.Modules.Auth.Data;

/// <summary>Used only by "dotnet ef" tooling to create the context without booting the API host.</summary>
internal sealed class AuthDbContextFactory : IDesignTimeDbContextFactory<AuthDbContext>
{
    public AuthDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseSqlServer(DesignTimeConnection.Resolve(), sql =>
                sql.MigrationsHistoryTable("__EFMigrationsHistory", AuthDbContext.Schema))
            .Options;

        return new AuthDbContext(options, new StaticTenantContext(), NoAuditContext.Instance);
    }
}
