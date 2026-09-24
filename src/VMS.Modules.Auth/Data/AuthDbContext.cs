using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using VMS.Modules.Auth.Domain;
using VMS.Shared.Auditing;
using VMS.Shared.Common;
using VMS.Shared.Time;

namespace VMS.Modules.Auth.Data;

internal sealed class AuthDbContext : DbContext, ITenantScopedDbContext
{
    internal const string Schema = "auth";

    private readonly IAuditContext _audit;

    public ITenantContext TenantContext { get; }

    public AuthDbContext(DbContextOptions<AuthDbContext> options, ITenantContext tenantContext, IAuditContext audit) : base(options)
    {
        TenantContext = tenantContext;
        _audit = audit;
    }

    internal DbSet<UserAccount> UserAccounts => Set<UserAccount>();
    internal DbSet<UserSession> UserSessions => Set<UserSession>();
    internal DbSet<Permission> Permissions => Set<Permission>();
    internal DbSet<Role> Roles => Set<Role>();
    internal DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    internal DbSet<UserRole> UserRoles => Set<UserRole>();
    internal DbSet<AccessDenialRecord> AccessDenials => Set<AccessDenialRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AuthDbContext).Assembly);
        modelBuilder.MapAuditEntries(); // the Core module owns the table; this context only writes to it
        modelBuilder.ApplyTenantQueryFilters(this);
        // Every filtered query carries WHERE TenantId = @tenant — make sure each table can seek on it.
        modelBuilder.ApplyTenantIndexes();
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.UseUtcDateTimes();   // every instant is stored, and read back, as UTC

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        // A user (filtered by tenant) references a Role (filtered by tenant-or-global); EF warns the
        // two filters differ. That is intended: a tenant's users may hold a global role.
        optionsBuilder.ConfigureWarnings(w =>
            w.Ignore(CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning));

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
