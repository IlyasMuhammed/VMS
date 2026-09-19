using Microsoft.EntityFrameworkCore;
using VMS.Modules.Tenancy.Domain;

namespace VMS.Modules.Tenancy.Data;

internal sealed class TenancyDbContext(DbContextOptions<TenancyDbContext> options) : DbContext(options)
{
    internal const string Schema = "tenancy";

    internal DbSet<Tenant> Tenants => Set<Tenant>();
    internal DbSet<SuperAdminUser> SuperAdminUsers => Set<SuperAdminUser>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

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

        modelBuilder.Entity<SuperAdminUser>(b =>
        {
            b.ToTable("SuperAdminUsers");
            b.HasKey(x => x.UserId);
            b.Property(x => x.UserId).ValueGeneratedNever();
        });
    }
}
