using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using VMS.Shared.Common;

namespace VMS.Modules.Notifications.Data;

/// <summary>Used only by "dotnet ef" tooling to create the context without booting the API host.</summary>
internal sealed class NotificationDbContextFactory : IDesignTimeDbContextFactory<NotificationDbContext>
{
    public NotificationDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<NotificationDbContext>()
            .UseSqlServer(DesignTimeConnection.Resolve(), sql =>
                sql.MigrationsHistoryTable("__EFMigrationsHistory", NotificationDbContext.Schema))
            .Options;

        return new NotificationDbContext(options, new StaticTenantContext());
    }
}
