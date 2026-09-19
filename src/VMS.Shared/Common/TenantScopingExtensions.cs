using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace VMS.Shared.Common;

/// <summary>
/// Implemented by every tenant-scoped DbContext. Exposing the injected <see cref="ITenantContext"/>
/// as an instance property lets the query filter close over <c>this</c> (the DbContext) instead of
/// the constructor parameter. EF Core compiles the model once per DbContext <em>type</em>, so a
/// filter that captured the parameter directly would stay stuck on whichever instance built the
/// model first. EF rewrites captured references to the model's own DbContext type so they resolve
/// to the currently-executing instance on every query.
/// </summary>
public interface ITenantScopedDbContext
{
    ITenantContext TenantContext { get; }
}

public static class TenantScopingExtensions
{
    /// <summary>
    /// Call at the top of every SaveChanges override. Only fills <c>TenantId</c> when it is unset, so
    /// a Super Admin creating a row for a <em>different</em> tenant is not overwritten with their own.
    /// </summary>
    public static void StampTenantScopedEntities(this DbContext db, ITenantContext tenantContext)
    {
        foreach (var entry in db.ChangeTracker.Entries())
        {
            if (entry.State != EntityState.Added) continue;

            switch (entry.Entity)
            {
                case ITenantScopedEntity { TenantId: var tenantId } scoped when tenantId == Guid.Empty:
                    scoped.TenantId = tenantContext.TenantId;
                    break;
                case IGloballyExemptTenantScopedEntity exempt when !exempt.IsGlobal && exempt.TenantId is null:
                    exempt.TenantId = tenantContext.TenantId;
                    break;
            }
        }
    }

    /// <summary>Call once from OnModelCreating as <c>modelBuilder.ApplyTenantQueryFilters(this)</c>.</summary>
    public static void ApplyTenantQueryFilters<TContext>(this ModelBuilder modelBuilder, TContext context)
        where TContext : DbContext, ITenantScopedDbContext
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var clrType = entityType.ClrType;

            if (typeof(ITenantScopedEntity).IsAssignableFrom(clrType))
            {
                typeof(TenantScopingExtensions)
                    .GetMethod(nameof(ApplyPlainFilter), BindingFlags.NonPublic | BindingFlags.Static)!
                    .MakeGenericMethod(clrType, typeof(TContext))
                    .Invoke(null, [modelBuilder, context]);
            }
            else if (typeof(IGloballyExemptTenantScopedEntity).IsAssignableFrom(clrType))
            {
                typeof(TenantScopingExtensions)
                    .GetMethod(nameof(ApplyExemptFilter), BindingFlags.NonPublic | BindingFlags.Static)!
                    .MakeGenericMethod(clrType, typeof(TContext))
                    .Invoke(null, [modelBuilder, context]);
            }
        }
    }

    /// <summary>
    /// Gives every tenant-scoped table an index that leads with <c>TenantId</c>, unless one exists —
    /// the query filter puts <c>WHERE TenantId = @tenant</c> on every query against these tables.
    /// </summary>
    public static void ApplyTenantIndexes(this ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var clrType = entityType.ClrType;

            var scoped = typeof(ITenantScopedEntity).IsAssignableFrom(clrType)
                      || typeof(IGloballyExemptTenantScopedEntity).IsAssignableFrom(clrType);
            if (!scoped) continue;

            if (entityType.IsOwned() || entityType.FindPrimaryKey() is null) continue;

            var tenantId = entityType.FindProperty(nameof(ITenantScopedEntity.TenantId));
            if (tenantId is null || LeadsWithTenantId(entityType)) continue;

            entityType.AddIndex(tenantId);
        }
    }

    private static bool LeadsWithTenantId(IMutableEntityType entityType)
    {
        static bool Leads(IReadOnlyList<IMutableProperty> properties) =>
            properties.Count > 0 && properties[0].Name == nameof(ITenantScopedEntity.TenantId);

        if (entityType.FindPrimaryKey() is { } key && Leads(key.Properties)) return true;
        return entityType.GetIndexes().Any(i => Leads(i.Properties));
    }

    private static void ApplyPlainFilter<TEntity, TContext>(ModelBuilder modelBuilder, TContext context)
        where TEntity : class, ITenantScopedEntity
        where TContext : DbContext, ITenantScopedDbContext
    {
        modelBuilder.Entity<TEntity>().HasQueryFilter(e =>
            context.TenantContext.IsSuperAdmin || e.TenantId == context.TenantContext.TenantId);
    }

    private static void ApplyExemptFilter<TEntity, TContext>(ModelBuilder modelBuilder, TContext context)
        where TEntity : class, IGloballyExemptTenantScopedEntity
        where TContext : DbContext, ITenantScopedDbContext
    {
        modelBuilder.Entity<TEntity>().HasQueryFilter(e =>
            context.TenantContext.IsSuperAdmin || e.IsGlobal || e.TenantId == context.TenantContext.TenantId);
    }
}
