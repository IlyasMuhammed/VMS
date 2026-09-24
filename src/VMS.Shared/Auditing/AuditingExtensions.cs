using Microsoft.EntityFrameworkCore;

namespace VMS.Shared.Auditing;

public static class AuditingExtensions
{
    public const string Schema = "core";
    public const string TableName = "AuditEntries";

    /// <summary>
    /// Call from OnModelCreating of every DbContext that saves audited entities. Only the Core module
    /// passes <paramref name="ownsTable"/> = true; everywhere else the table is mapped so the audit row
    /// can be written in the same save, but migrations leave it alone.
    /// </summary>
    public static void MapAuditEntries(this ModelBuilder modelBuilder, bool ownsTable = false)
    {
        modelBuilder.Entity<AuditEntry>(b =>
        {
            b.ToTable(TableName, Schema, t =>
            {
                if (!ownsTable) t.ExcludeFromMigrations();
            });
            b.HasKey(x => x.AuditEntryID);
            b.Property(x => x.Entity).HasMaxLength(100).IsRequired();
            b.Property(x => x.RecordId).HasMaxLength(100).IsRequired();
            b.Property(x => x.Action).HasMaxLength(50).IsRequired();
            b.Property(x => x.RootEntity).HasMaxLength(100);
            b.Property(x => x.RootRecordId).HasMaxLength(100);
            b.Property(x => x.Field).HasMaxLength(100);
            b.Property(x => x.OldValue).HasColumnType("nvarchar(max)");
            b.Property(x => x.NewValue).HasColumnType("nvarchar(max)");
            b.Property(x => x.Reason).HasMaxLength(500);
            b.Property(x => x.RequiredPermission).HasMaxLength(100);
            b.Property(x => x.UserName).HasMaxLength(200);
            b.Property(x => x.IpAddress).HasMaxLength(64);

            // "Everything that happened to this record" (history tab) and "everything in this period" (viewer).
            b.HasIndex(x => new { x.TenantId, x.Entity, x.RecordId, x.OccurredAt });
            b.HasIndex(x => new { x.TenantId, x.RootEntity, x.RootRecordId, x.OccurredAt });   // everything that happened to one partner, vehicle, …
            b.HasIndex(x => new { x.TenantId, x.OccurredAt });
            b.HasIndex(x => new { x.TenantId, x.UserId, x.OccurredAt });
            b.HasIndex(x => x.GroupId);
        });
    }

    /// <summary>
    /// Saves, and writes the audit trail for what changed in the same transaction (BR-BP-022: no change
    /// without its audit row, no audit row without its change). Call it from every SaveChanges override,
    /// after stamping the tenant, passing the base save as <paramref name="save"/>.
    /// <para>
    /// When every record id is already known the audit rows join the one save, which EF makes atomic. A
    /// created row whose key the database assigns is saved first so its id exists, then the audit rows
    /// are saved, both inside one transaction (the caller's if there is one, otherwise a new one).
    /// </para>
    /// </summary>
    public static async Task<int> SaveAuditedAsync(this DbContext db, IAuditContext audit, Guid fallbackTenantId,
        bool acceptAllChangesOnSuccess, Func<bool, CancellationToken, Task<int>> save, CancellationToken cancellationToken = default)
    {
        if (audit.Actor is not { } actor || db.Model.FindEntityType(typeof(AuditEntry)) is null)
            return await save(acceptAllChangesOnSuccess, cancellationToken);

        db.ChangeTracker.DetectChanges();
        var capture = AuditCapture.From(db);
        var noteCount = audit.PendingNotes.Count;
        if (capture.IsEmpty && noteCount == 0)
            return await save(acceptAllChangesOnSuccess, cancellationToken);

        var now = DateTime.UtcNow;

        if (!capture.NeedsKeysAfterSave)
        {
            var rows = capture.Build(audit, actor, fallbackTenantId, now);
            db.Set<AuditEntry>().AddRange(rows);
            try
            {
                var saved = await save(acceptAllChangesOnSuccess, cancellationToken);
                audit.ClearNotes(noteCount);
                return saved;
            }
            catch
            {
                // A caller that retries must not find last attempt's audit rows still waiting to be saved.
                foreach (var row in rows) db.Entry(row).State = EntityState.Detached;
                throw;
            }
        }

        async Task<int> TwoPhase()
        {
            var saved = await save(true, cancellationToken);
            db.Set<AuditEntry>().AddRange(capture.Build(audit, actor, fallbackTenantId, now));
            await save(true, cancellationToken);
            audit.ClearNotes(noteCount);
            return saved;
        }

        if (db.Database.CurrentTransaction is not null) return await TwoPhase();

        // Retrying a half-finished pair of saves would insert the business rows twice, so a transient
        // failure here is surfaced (nothing was saved) rather than retried.
        var attempts = 0;
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            if (++attempts > 1)
                throw new InvalidOperationException("The database connection was interrupted while saving. Nothing was saved; please try again.");

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var saved = await TwoPhase();
            await transaction.CommitAsync(cancellationToken);
            return saved;
        });
    }

    /// <summary>The synchronous twin of <see cref="SaveAuditedAsync"/>.</summary>
    public static int SaveAudited(this DbContext db, IAuditContext audit, Guid fallbackTenantId,
        bool acceptAllChangesOnSuccess, Func<bool, int> save)
    {
        if (audit.Actor is not { } actor || db.Model.FindEntityType(typeof(AuditEntry)) is null)
            return save(acceptAllChangesOnSuccess);

        db.ChangeTracker.DetectChanges();
        var capture = AuditCapture.From(db);
        var noteCount = audit.PendingNotes.Count;
        if (capture.IsEmpty && noteCount == 0)
            return save(acceptAllChangesOnSuccess);

        var now = DateTime.UtcNow;

        if (!capture.NeedsKeysAfterSave)
        {
            var rows = capture.Build(audit, actor, fallbackTenantId, now);
            db.Set<AuditEntry>().AddRange(rows);
            try
            {
                var saved = save(acceptAllChangesOnSuccess);
                audit.ClearNotes(noteCount);
                return saved;
            }
            catch
            {
                foreach (var row in rows) db.Entry(row).State = EntityState.Detached;
                throw;
            }
        }

        int TwoPhase()
        {
            var saved = save(true);
            db.Set<AuditEntry>().AddRange(capture.Build(audit, actor, fallbackTenantId, now));
            save(true);
            audit.ClearNotes(noteCount);
            return saved;
        }

        if (db.Database.CurrentTransaction is not null) return TwoPhase();

        var attempts = 0;
        return db.Database.CreateExecutionStrategy().Execute(() =>
        {
            if (++attempts > 1)
                throw new InvalidOperationException("The database connection was interrupted while saving. Nothing was saved; please try again.");

            using var transaction = db.Database.BeginTransaction();
            var saved = TwoPhase();
            transaction.Commit();
            return saved;
        });
    }
}
