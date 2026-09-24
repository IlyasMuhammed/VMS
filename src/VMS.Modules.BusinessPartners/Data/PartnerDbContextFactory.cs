using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using VMS.Shared.Auditing;
using VMS.Shared.Common;

namespace VMS.Modules.BusinessPartners.Data;

/// <summary>Used only by "dotnet ef" tooling to create the context without booting the API host.</summary>
internal sealed class PartnerDbContextFactory : IDesignTimeDbContextFactory<PartnerDbContext>
{
    public PartnerDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PartnerDbContext>()
            .UseSqlServer(DesignTimeConnection.Resolve(), sql =>
                sql.MigrationsHistoryTable("__EFMigrationsHistory", PartnerDbContext.Schema))
            .Options;

        return new PartnerDbContext(options, new StaticTenantContext(), NoAuditContext.Instance);
    }
}
