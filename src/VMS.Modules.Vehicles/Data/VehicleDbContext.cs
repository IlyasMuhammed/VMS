using Microsoft.EntityFrameworkCore;
using VMS.Modules.Vehicles.Domain;
using VMS.Shared.Auditing;
using VMS.Shared.Common;
using VMS.Shared.Time;

namespace VMS.Modules.Vehicles.Data;

internal sealed class VehicleDbContext : DbContext, ITenantScopedDbContext
{
    internal const string Schema = "veh";

    private readonly IAuditContext _audit;

    public ITenantContext TenantContext { get; }

    public VehicleDbContext(DbContextOptions<VehicleDbContext> options, ITenantContext tenantContext, IAuditContext audit) : base(options)
    {
        TenantContext = tenantContext;
        _audit = audit;
    }

    internal DbSet<Vehicle> Vehicles => Set<Vehicle>();
    internal DbSet<VehicleRelation> Relations => Set<VehicleRelation>();
    internal DbSet<VehicleLifecycleEntry> Lifecycle => Set<VehicleLifecycleEntry>();
    internal DbSet<VehicleAttachedItem> Items => Set<VehicleAttachedItem>();
    internal DbSet<OdometerReading> Odometer => Set<OdometerReading>();
    internal DbSet<DriverAssignment> Assignments => Set<DriverAssignment>();
    internal DbSet<VehicleAcquisition> Acquisitions => Set<VehicleAcquisition>();
    internal DbSet<VehicleTransaction> Transactions => Set<VehicleTransaction>();
    internal DbSet<VehicleFinanceAgreement> Agreements => Set<VehicleFinanceAgreement>();
    internal DbSet<VehicleInstallment> Installments => Set<VehicleInstallment>();
    internal DbSet<VehicleRecurringCharge> RecurringCharges => Set<VehicleRecurringCharge>();
    internal DbSet<VehicleRecurringChargeEntry> RecurringChargeEntries => Set<VehicleRecurringChargeEntry>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.UseUtcDateTimes();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(VehicleDbContext).Assembly);
        modelBuilder.MapAuditEntries();   // the Core module owns the table; this context only writes to it
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
