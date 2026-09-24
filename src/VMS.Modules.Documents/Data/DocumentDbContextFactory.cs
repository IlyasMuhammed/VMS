using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using VMS.Shared.Auditing;
using VMS.Shared.Common;

namespace VMS.Modules.Documents.Data;

/// <summary>Used only by "dotnet ef" tooling to create the context without booting the API host.</summary>
internal sealed class DocumentDbContextFactory : IDesignTimeDbContextFactory<DocumentDbContext>
{
    public DocumentDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<DocumentDbContext>()
            .UseSqlServer(DesignTimeConnection.Resolve(), sql =>
                sql.MigrationsHistoryTable("__EFMigrationsHistory", DocumentDbContext.Schema))
            .Options;

        return new DocumentDbContext(options, new StaticTenantContext(), NoAuditContext.Instance);
    }
}
