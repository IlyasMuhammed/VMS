using Microsoft.EntityFrameworkCore;
using VMS.Modules.Tenancy.Domain;
using VMS.Shared.Auditing;
using VMS.Shared.Common;
using VMS.Shared.Time;

namespace VMS.Modules.Tenancy.Data;

internal sealed class TenancyDbContext(DbContextOptions<TenancyDbContext> options, ITenantContext tenantContext, IAuditContext audit)
    : DbContext(options)
{
    internal const string Schema = "tenancy";

    internal DbSet<Tenant> Tenants => Set<Tenant>();
    internal DbSet<TenantLogo> TenantLogos => Set<TenantLogo>();
    internal DbSet<SuperAdminUser> SuperAdminUsers => Set<SuperAdminUser>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.UseUtcDateTimes();   // every instant is stored, and read back, as UTC

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.MapAuditEntries(); // the Core module owns the table; this context only writes to it

        modelBuilder.Entity<Tenant>(b =>
        {
            b.ToTable("Tenants");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.TenantCode).HasMaxLength(30).IsRequired();
            b.HasIndex(x => x.TenantCode).IsUnique();
            b.Property(x => x.TenantName).HasMaxLength(200).IsRequired();
            b.Property(x => x.IsActive).HasDefaultValue(true);
            b.Property(x => x.ContactEmail).HasMaxLength(256);
            b.Property(x => x.ContactPhone).HasMaxLength(30);
            b.Property(x => x.Address).HasMaxLength(500);
            b.Property(x => x.Country).HasMaxLength(100);
            b.Property(x => x.TimeZone).HasMaxLength(100);
        });

        modelBuilder.Entity<TenantLogo>(b =>
        {
            b.ToTable("TenantLogos");
            b.HasKey(x => new { x.TenantId, x.Variant });
            b.Property(x => x.Variant).HasMaxLength(5).IsRequired();
            b.Property(x => x.ContentType).HasMaxLength(50).IsRequired();
            b.Property(x => x.Content).HasColumnType("varbinary(max)").IsRequired();
            b.Property(x => x.Version).HasMaxLength(64).IsRequired();
            b.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SuperAdminUser>(b =>
        {
            b.ToTable("SuperAdminUsers");
            b.HasKey(x => x.UserId);
            b.Property(x => x.UserId).ValueGeneratedNever();
        });
    }

    // Tenant rows are not filtered by tenant, so audit rows for them are filed under the acting user's tenant.
    public override int SaveChanges(bool acceptAllChangesOnSuccess) =>
        this.SaveAudited(audit, tenantContext.TenantId, acceptAllChangesOnSuccess, a => base.SaveChanges(a));

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default) =>
        this.SaveAuditedAsync(audit, tenantContext.TenantId, acceptAllChangesOnSuccess,
            (a, ct) => base.SaveChangesAsync(a, ct), cancellationToken);
}
