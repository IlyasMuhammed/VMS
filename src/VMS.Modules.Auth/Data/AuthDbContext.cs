using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using VMS.Modules.Auth.Domain;
using VMS.Shared.Common;

namespace VMS.Modules.Auth.Data;

internal sealed class AuthDbContext : DbContext, ITenantScopedDbContext
{
    internal const string Schema = "auth";

    public ITenantContext TenantContext { get; }

    public AuthDbContext(DbContextOptions<AuthDbContext> options, ITenantContext tenantContext) : base(options) =>
        TenantContext = tenantContext;

    internal DbSet<UserAccount> UserAccounts => Set<UserAccount>();
    internal DbSet<UserSession> UserSessions => Set<UserSession>();
    internal DbSet<Permission> Permissions => Set<Permission>();
    internal DbSet<Role> Roles => Set<Role>();
    internal DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AuthDbContext).Assembly);
        modelBuilder.ApplyTenantQueryFilters(this);
        // Every filtered query carries WHERE TenantId = @tenant — make sure each table can seek on it.
        modelBuilder.ApplyTenantIndexes();
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        // A user (filtered by tenant) references a Role (filtered by tenant-or-global); EF warns the
        // two filters differ. That is intended: a tenant's users may hold a global role.
        optionsBuilder.ConfigureWarnings(w =>
            w.Ignore(CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning));

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
