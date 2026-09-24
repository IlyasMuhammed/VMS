using Microsoft.EntityFrameworkCore;
using VMS.Modules.Core.Domain;
using VMS.Shared.Auditing;
using VMS.Shared.Common;
using VMS.Shared.Time;

namespace VMS.Modules.Core.Data;

/// <summary>
/// Platform services every module builds on: the audit log today; numbering series, lookups and file
/// metadata as the foundation tasks land. Owns the <c>core</c> schema.
/// </summary>
internal sealed class CoreDbContext : DbContext, ITenantScopedDbContext
{
    internal const string Schema = AuditingExtensions.Schema;

    private readonly IAuditContext _audit;

    public ITenantContext TenantContext { get; }

    public CoreDbContext(DbContextOptions<CoreDbContext> options, ITenantContext tenantContext, IAuditContext audit) : base(options)
    {
        TenantContext = tenantContext;
        _audit = audit;
    }

    internal DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    internal DbSet<NumberSeries> NumberSeries => Set<NumberSeries>();
    internal DbSet<NumberSeriesCounter> NumberSeriesCounters => Set<NumberSeriesCounter>();
    internal DbSet<LookupValue> LookupValues => Set<LookupValue>();
    internal DbSet<Branch> Branches => Set<Branch>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.UseUtcDateTimes();   // every instant is stored, and read back, as UTC

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CoreDbContext).Assembly);
        modelBuilder.MapAuditEntries(ownsTable: true);
        modelBuilder.ApplyTenantQueryFilters(this);
        modelBuilder.ApplyTenantIndexes();
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        this.StampTenantScopedEntities(TenantContext);
        return this.SaveAudited(_audit, TenantContext.TenantId, acceptAllChangesOnSuccess, a => base.SaveChanges(a));
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        this.StampTenantScopedEntities(TenantContext);
        return this.SaveAuditedAsync(_audit, TenantContext.TenantId, acceptAllChangesOnSuccess,
            (a, ct) => base.SaveChangesAsync(a, ct), cancellationToken);
    }
}
