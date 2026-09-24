using Microsoft.EntityFrameworkCore;
using VMS.Modules.Notifications.Domain;
using VMS.Shared.Common;
using VMS.Shared.Time;

namespace VMS.Modules.Notifications.Data;

internal sealed class NotificationDbContext : DbContext, ITenantScopedDbContext
{
    internal const string Schema = "notif";

    public ITenantContext TenantContext { get; }

    public NotificationDbContext(DbContextOptions<NotificationDbContext> options, ITenantContext tenantContext) : base(options)
    {
        TenantContext = tenantContext;
    }

    internal DbSet<NotificationRule> Rules => Set<NotificationRule>();
    internal DbSet<Notification> Notifications => Set<Notification>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.UseUtcDateTimes();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NotificationDbContext).Assembly);
        modelBuilder.ApplyTenantQueryFilters(this);
        modelBuilder.ApplyTenantIndexes();
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        this.StampTenantScopedEntities(TenantContext);
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        this.StampTenantScopedEntities(TenantContext);
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
}
