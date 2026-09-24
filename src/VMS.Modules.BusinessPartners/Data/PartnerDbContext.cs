using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using VMS.Modules.BusinessPartners.Domain;
using VMS.Shared.Auditing;
using VMS.Shared.Common;
using VMS.Shared.Time;

namespace VMS.Modules.BusinessPartners.Data;

internal sealed class PartnerDbContext : DbContext, ITenantScopedDbContext
{
    internal const string Schema = "bp";

    private readonly IAuditContext _audit;

    public ITenantContext TenantContext { get; }

    public PartnerDbContext(DbContextOptions<PartnerDbContext> options, ITenantContext tenantContext, IAuditContext audit) : base(options)
    {
        TenantContext = tenantContext;
        _audit = audit;
    }

    internal DbSet<BusinessPartner> Partners => Set<BusinessPartner>();
    internal DbSet<BusinessPartnerRole> Roles => Set<BusinessPartnerRole>();
    internal DbSet<BpDriverDetail> DriverDetails => Set<BpDriverDetail>();
    internal DbSet<BpVendorDetail> VendorDetails => Set<BpVendorDetail>();
    internal DbSet<BpCustomerDetail> CustomerDetails => Set<BpCustomerDetail>();
    internal DbSet<BpContact> Contacts => Set<BpContact>();
    internal DbSet<BpAddress> Addresses => Set<BpAddress>();
    internal DbSet<BpBankAccount> BankAccounts => Set<BpBankAccount>();
    internal DbSet<BpRoleLog> RoleLog => Set<BpRoleLog>();
    internal DbSet<BpStatusLog> StatusLog => Set<BpStatusLog>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.UseUtcDateTimes();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PartnerDbContext).Assembly);
        modelBuilder.MapAuditEntries();   // the Core module owns the table; this context only writes to it
        modelBuilder.ApplyTenantQueryFilters(this);
        modelBuilder.ApplyTenantIndexes();
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        // A child (filtered by tenant) requires its partner (also filtered); EF warns the filters could differ. They are the same filter.
        optionsBuilder.ConfigureWarnings(w => w.Ignore(CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning));

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
